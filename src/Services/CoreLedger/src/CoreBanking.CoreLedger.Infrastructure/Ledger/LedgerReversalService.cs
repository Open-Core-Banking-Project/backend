using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Ledger;

public sealed record FullLedgerReversalCommand(
    PostingIdempotencyScope IdempotencyScope,
    string OriginalFinancialTransactionId,
    string FinancialReversalId,
    string ReversalFinancialTransactionId,
    string ReversalTransactionReference,
    string ReversalJournalEntryId,
    string ReversalJournalReference,
    LedgerPostingControlContext ControlContext,
    string ReasonCode,
    string EvidenceReference,
    ReversalFinalityEvidence FinalityEvidence,
    string ActorId,
    string ActorType,
    string CorrelationId,
    string? CausationId = null,
    ReversalApprovalEvidence? Approval = null);

/// <summary>
/// Protected full-reversal entry point. It derives opposite lines from authoritative posted rows and delegates the
/// final commit to the same atomic posting boundary. No HTTP endpoint exposes this service.
/// </summary>
public sealed class LedgerReversalService
{
    private readonly CoreLedgerDbContext _context;
    private readonly AtomicLedgerPostingService _posting;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public LedgerReversalService(
        CoreLedgerDbContext context,
        AtomicLedgerPostingService posting,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _posting = posting ?? throw new ArgumentNullException(nameof(posting));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<LedgerPostingResult> ReverseFullAsync(
        FullLedgerReversalCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);
        try
        {
            FinancialTransactionRecord original = await _context.FinancialTransactions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    record => record.FinancialTransactionId == command.OriginalFinancialTransactionId,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new LedgerPostingException("financial_reversal_original_not_found");
            JournalEntryRecord originalJournal = await _context.JournalEntries
                .AsNoTracking()
                .SingleAsync(
                    record => record.FinancialTransactionId == original.FinancialTransactionId,
                    cancellationToken)
                .ConfigureAwait(false);
            JournalLineRecord[] originalLines = await _context.JournalLines
                .AsNoTracking()
                .Where(record => record.JournalEntryId == originalJournal.JournalEntryId)
                .OrderBy(record => record.Sequence)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            BusinessDateControlRecord businessDate = await _context.BusinessDateControls
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new LedgerPostingException("financial_reversal_business_date_unavailable");

            DateTimeOffset now = _timeProvider.GetUtcNow();
            FinancialDates dates = FinancialDates.Create(
                now,
                businessDate.CurrentBusinessDate,
                businessDate.CurrentBusinessDate,
                businessDate.CurrentBusinessDate);
            string requestHash = ComputeRequestHash(command);
            FinancialTransaction transaction = FinancialTransaction.CreateReadyToPost(
                command.ReversalFinancialTransactionId,
                command.ReversalTransactionReference,
                original.InstitutionId,
                "core-ledger-reversal",
                command.ReversalTransactionReference,
                original.EventCode,
                requestHash,
                dates);
            AccountingScope scope = new(
                originalJournal.InstitutionId,
                originalJournal.LegalEntityCode,
                CurrencyCode.Parse(originalJournal.CurrencyCode));
            JournalEntry journal = JournalEntry.Create(
                command.ReversalJournalEntryId,
                transaction.Id,
                command.ReversalJournalReference,
                originalJournal.MappingVersionId,
                scope,
                dates,
                originalLines.Select(line => JournalLine.Create(
                    command.ReversalFinancialTransactionId + "-line-" + line.Sequence,
                    line.Sequence,
                    line.LedgerAccountId,
                    Opposite(line.Direction),
                    ParseComponent(line.ComponentType),
                    Money.Create(line.Amount, line.CurrencyCode),
                    scope,
                    command.ReasonCode)));
            LedgerReversalContext reversal = new(
                original.FinancialTransactionId,
                command.FinancialReversalId,
                command.ReasonCode,
                command.EvidenceReference,
                businessDate.CurrentBusinessDate,
                command.FinalityEvidence,
                command.Approval);

            return await _posting.PostAsync(
                new AtomicLedgerPostingCommand(
                    command.IdempotencyScope,
                    transaction,
                    journal,
                    command.ControlContext,
                    command.ActorId,
                    command.ActorType,
                    command.CorrelationId,
                    command.CausationId,
                    Reversal: reversal),
                cancellationToken).ConfigureAwait(false);
        }
        catch (LedgerPostingException exception)
        {
            await RecordRejectionAsync(command, exception.Code).ConfigureAwait(false);
            throw;
        }
    }

    public static string ComputeRequestHash(FullLedgerReversalCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        string json = JsonSerializer.Serialize(new
        {
            command.OriginalFinancialTransactionId,
            command.FinancialReversalId,
            command.ReversalFinancialTransactionId,
            command.ReversalTransactionReference,
            command.ReversalJournalEntryId,
            command.ReversalJournalReference,
            command.ReasonCode,
            command.EvidenceReference,
            FinalityEvidence = command.FinalityEvidence.ToString(),
        });
        return CanonicalFinancialRequest.Create(json).Hash;
    }

    private async Task RecordRejectionAsync(FullLedgerReversalCommand command, string code)
    {
        _context.ChangeTracker.Clear();
        DateTimeOffset occurredAt = _timeProvider.GetUtcNow();
        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = command.IdempotencyScope.InstitutionId.Value,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = command.ActorId,
            ActorType = command.ActorType,
            Permission = "ledger.transaction.reverse",
            Action = "ledger.transaction.reverse",
            ResourceType = "ledger.financial_transaction",
            ResourceId = command.OriginalFinancialTransactionId,
            Outcome = "rejected",
            Reason = code,
            CorrelationId = command.CorrelationId,
            AfterReference = command.EvidenceReference,
            OccurredAt = occurredAt,
        });
        await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void Validate(FullLedgerReversalCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.IdempotencyScope);
        ArgumentNullException.ThrowIfNull(command.ControlContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.OriginalFinancialTransactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.FinancialReversalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ReversalFinancialTransactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ReversalTransactionReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ReversalJournalEntryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ReversalJournalReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ReasonCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.EvidenceReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ActorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ActorType);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        if (!string.Equals(
                _tenantContext.CurrentInstitution?.Value,
                command.IdempotencyScope.InstitutionId.Value,
                StringComparison.Ordinal))
        {
            throw new LedgerPostingException("institution_context_mismatch");
        }
    }

    private static DebitCreditIndicator Opposite(string direction) => direction switch
    {
        "debit" => DebitCreditIndicator.Credit,
        "credit" => DebitCreditIndicator.Debit,
        _ => throw new LedgerPostingException("journal_line_direction_invalid"),
    };

    private static AccountingComponentType ParseComponent(string value) => value switch
    {
        "principal" => AccountingComponentType.Principal,
        "fee" => AccountingComponentType.Fee,
        "tax" => AccountingComponentType.Tax,
        "commission" => AccountingComponentType.Commission,
        "penalty" => AccountingComponentType.Penalty,
        "interest" => AccountingComponentType.Interest,
        "waiver" => AccountingComponentType.Waiver,
        "rounding" => AccountingComponentType.Rounding,
        _ => throw new LedgerPostingException("accounting_component_invalid"),
    };
}
