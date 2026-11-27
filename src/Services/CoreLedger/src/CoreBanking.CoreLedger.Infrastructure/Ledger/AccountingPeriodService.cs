using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Ledger;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CoreBanking.CoreLedger.Infrastructure.Ledger;

/// <summary>
/// Owns the approved accounting-period lifecycle. Every transition runs in its own transaction under the same advisory
/// lock posting uses, checks optimistic version, records append-only history and audit evidence, and keeps the
/// soft-close/reopen allow-lists empty unless Finance publishes them. Decision: P1-E05-D13.
/// </summary>
public sealed class AccountingPeriodService : IAccountingPeriodService
{
    private readonly CoreLedgerDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public AccountingPeriodService(
        CoreLedgerDbContext context,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<AccountingPeriodResult> OpenAsync(
        OpenAccountingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureTenant(command.InstitutionId);
        string periodCode = PeriodCode(command.Year, command.Month);
        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await PostingDateGuard.AcquirePeriodLockAsync(
            _context, command.InstitutionId.Value, LegalEntity(command.LegalEntityCode), periodCode, cancellationToken)
            .ConfigureAwait(false);

        AccountingPeriodRecord? existing = await FindAsync(
            command.InstitutionId.Value, LegalEntity(command.LegalEntityCode), periodCode, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return ToResult(existing, replayed: true);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        AccountingPeriodRecord period = new()
        {
            InstitutionId = command.InstitutionId.Value,
            LegalEntityCode = LegalEntity(command.LegalEntityCode),
            PeriodCode = periodCode,
            Status = PostingDateGuard.StatusToken(AccountingPeriodStatus.Open),
            AllowedEventCodesJson = PostingDateGuard.WriteAllowList([]),
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _context.AccountingPeriods.Add(period);
        AddHistory(period, 1, AccountingPeriodStatus.Open, AccountingPeriodStatus.Open, command.ActorId.Value, null, null, command.Note, now);
        AddAudit(period, command.ActorId.Value, "ledger.period.open", "succeeded", command.Note, command.CorrelationId, now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return ToResult(period, replayed: false);
    }

    public async Task<AccountingPeriodResult> SoftCloseAsync(
        SoftCloseAccountingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureTenant(command.InstitutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ApprovalReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ReasonCode);

        return await TransitionAsync(
            command.InstitutionId,
            command.LegalEntityCode,
            command.Year,
            command.Month,
            command.ExpectedVersion,
            command.ActorId.Value,
            command.CorrelationId,
            AccountingPeriodStatus.SoftClosed,
            period =>
            {
                string[] allowList = AccountingPeriodRules.NormaliseAllowList(command.AllowedEventCodes);
                period.AllowedEventCodesJson = PostingDateGuard.WriteAllowList(allowList);
                period.CloseApprovalReference = command.ApprovalReference.Trim();
            },
            command.ReasonCode,
            "ledger.period.soft_close",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountingPeriodResult> CloseAsync(
        CloseAccountingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureTenant(command.InstitutionId);
        AccountingPeriodCloseEvidence evidence = new(
            command.DebitTotal,
            command.CreditTotal,
            command.BlockingReconciliationExceptions,
            command.RequiredProcessingStepsCompleted,
            command.OutboxHealthy,
            command.ApprovalReference,
            command.SummaryHash);
        AccountingPeriodRules.EnsureCloseEvidence(evidence);

        return await TransitionAsync(
            command.InstitutionId,
            command.LegalEntityCode,
            command.Year,
            command.Month,
            command.ExpectedVersion,
            command.ActorId.Value,
            command.CorrelationId,
            AccountingPeriodStatus.Closed,
            period =>
            {
                period.ClosedDebitTotal = command.DebitTotal;
                period.ClosedCreditTotal = command.CreditTotal;
                period.CloseSummaryHash = command.SummaryHash.Trim();
                period.CloseApprovalReference = command.ApprovalReference.Trim();
                period.ClosedAt = _timeProvider.GetUtcNow();
                period.RecloseReconciliationReference = null;
                period.ReclosedAt = null;
            },
            command.SummaryHash,
            "ledger.period.close",
            cancellationToken,
            allowFromReopened: false).ConfigureAwait(false);
    }

    public async Task<AccountingPeriodResult> ReopenAsync(
        ReopenAccountingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureTenant(command.InstitutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ReasonCode);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        AccountingPeriodApproval[] approvals = (command.Approvals ?? throw new ArgumentNullException(nameof(command)))
            .Select(item => new AccountingPeriodApproval(item.ApproverActorId, item.ApprovalReference, item.ApprovedAt))
            .ToArray();
        AccountingPeriodRules.EnsureDistinctApprovers(command.ActorId.Value, approvals, required: 2);
        AccountingPeriodRules.EnsureReopenWindow(now, command.ExpiresAt);
        string[] allowList = AccountingPeriodRules.NormaliseAllowList(command.AllowedEventCodes);

        return await TransitionAsync(
            command.InstitutionId,
            command.LegalEntityCode,
            command.Year,
            command.Month,
            command.ExpectedVersion,
            command.ActorId.Value,
            command.CorrelationId,
            AccountingPeriodStatus.Reopened,
            period =>
            {
                period.AllowedEventCodesJson = PostingDateGuard.WriteAllowList(allowList);
                period.ReopenExpiresAt = command.ExpiresAt;
                period.ReopenReasonCode = command.ReasonCode.Trim();
                period.CloseApprovalReference = approvals[0].ApprovalReference.Trim();
            },
            command.ReasonCode,
            "ledger.period.reopen",
            cancellationToken,
            allowFromReopened: false).ConfigureAwait(false);
    }

    public async Task<AccountingPeriodResult> RecloseAsync(
        RecloseAccountingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureTenant(command.InstitutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ReconciliationReference);

        return await TransitionAsync(
            command.InstitutionId,
            command.LegalEntityCode,
            command.Year,
            command.Month,
            command.ExpectedVersion,
            command.ActorId.Value,
            command.CorrelationId,
            AccountingPeriodStatus.Closed,
            period =>
            {
                period.ReclosedAt = _timeProvider.GetUtcNow();
                period.RecloseReconciliationReference = command.ReconciliationReference.Trim();
            },
            command.ReconciliationReference,
            "ledger.period.reclose",
            cancellationToken,
            allowFromReopened: true).ConfigureAwait(false);
    }

    public async Task<AccountingPeriodResult> LockAsync(
        LockAccountingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureTenant(command.InstitutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ApprovalReference);

        return await TransitionAsync(
            command.InstitutionId,
            command.LegalEntityCode,
            command.Year,
            command.Month,
            command.ExpectedVersion,
            command.ActorId.Value,
            command.CorrelationId,
            AccountingPeriodStatus.Locked,
            period =>
            {
                period.LockedAt = _timeProvider.GetUtcNow();
                period.LockApprovalReference = command.ApprovalReference.Trim();
            },
            command.ApprovalReference,
            "ledger.period.lock",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountingPeriodResult> GetAsync(
        InstitutionId institutionId,
        string legalEntityCode,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        string periodCode = PeriodCode(year, month);
        AccountingPeriodRecord period = await _context.AccountingPeriods
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId.Value
                    && record.LegalEntityCode == legalEntityCode.Trim()
                    && record.PeriodCode == periodCode,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LedgerPostingException("accounting_period_not_found");
        return ToResult(period, replayed: false);
    }

    public async Task<IReadOnlyList<RecalculationHandoffResult>> GetRecalculationHandoffsAsync(
        InstitutionId institutionId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        string periodCode = PeriodCode(year, month);
        RecalculationHandoffRecord[] handoffs = await _context.RecalculationHandoffs
            .AsNoTracking()
            .Where(record => record.InstitutionId == institutionId.Value && record.PeriodCode == periodCode)
            .OrderBy(record => record.RecordedAt)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return handoffs.Select(handoff => new RecalculationHandoffResult(
                handoff.InstitutionId,
                handoff.LegalEntityCode,
                handoff.PeriodCode,
                handoff.HandoffId,
                handoff.FinancialTransactionId,
                handoff.ValueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                handoff.AgeDays,
                handoff.ReasonCode,
                handoff.EvidenceReference,
                handoff.ApprovalReference,
                handoff.RecordedAt))
            .ToArray();
    }

    private async Task<AccountingPeriodResult> TransitionAsync(
        InstitutionId institutionId,
        string legalEntityCode,
        int year,
        int month,
        long expectedVersion,
        string actorId,
        string correlationId,
        AccountingPeriodStatus target,
        Action<AccountingPeriodRecord> apply,
        string? reason,
        string action,
        CancellationToken cancellationToken,
        bool allowFromReopened = false)
    {
        string legalEntity = LegalEntity(legalEntityCode);
        string periodCode = PeriodCode(year, month);
        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await PostingDateGuard.AcquirePeriodLockAsync(
            _context, institutionId.Value, legalEntity, periodCode, cancellationToken).ConfigureAwait(false);

        AccountingPeriodRecord period = await FindAsync(institutionId.Value, legalEntity, periodCode, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LedgerPostingException("accounting_period_not_found");
        if (period.Version != expectedVersion)
        {
            throw new LedgerPostingException("accounting_period_version_stale");
        }

        AccountingPeriodStatus current = PostingDateGuard.ParseStatus(period.Status);
        if (allowFromReopened)
        {
            if (current != AccountingPeriodStatus.Reopened)
            {
                throw new LedgerPostingException("accounting_period_transition_invalid");
            }
        }
        else if (current == AccountingPeriodStatus.Reopened)
        {
            throw new LedgerPostingException("accounting_period_reopen_reclose_required");
        }

        AccountingPeriodRules.EnsureTransition(current, target);
        apply(period);
        period.Status = PostingDateGuard.StatusToken(target);
        period.Version++;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        period.UpdatedAt = now;
        AddHistory(period, checked((int)period.Version), current, target, actorId, period.CloseApprovalReference, period.AllowedEventCodesJson, reason, now);
        AddAudit(period, actorId, action, "succeeded", reason, correlationId, now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return ToResult(period, replayed: false);
    }

    private Task<AccountingPeriodRecord?> FindAsync(
        string institutionId,
        string legalEntityCode,
        string periodCode,
        CancellationToken cancellationToken) =>
        _context.AccountingPeriods.SingleOrDefaultAsync(
            record => record.InstitutionId == institutionId
                && record.LegalEntityCode == legalEntityCode
                && record.PeriodCode == periodCode,
            cancellationToken);

    private void AddHistory(
        AccountingPeriodRecord period,
        int sequence,
        AccountingPeriodStatus from,
        AccountingPeriodStatus to,
        string actorId,
        string? approvalReference,
        string? allowedEventCodesJson,
        string? reason,
        DateTimeOffset now)
    {
        _context.AccountingPeriodHistory.Add(new AccountingPeriodHistoryRecord
        {
            InstitutionId = period.InstitutionId,
            LegalEntityCode = period.LegalEntityCode,
            PeriodCode = period.PeriodCode,
            Sequence = sequence,
            FromStatus = PostingDateGuard.StatusToken(from),
            ToStatus = PostingDateGuard.StatusToken(to),
            ActorId = actorId,
            ApprovalReference = approvalReference,
            AllowedEventCodesJson = allowedEventCodesJson,
            ReasonCode = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            OccurredAt = now,
        });
    }

    private void AddAudit(
        AccountingPeriodRecord period,
        string actorId,
        string action,
        string outcome,
        string? reason,
        string correlationId,
        DateTimeOffset now) =>
        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = period.InstitutionId,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = actorId,
            ActorType = "staff",
            Permission = action,
            Action = action,
            ResourceType = "ledger.accounting_period",
            ResourceId = period.LegalEntityCode + ":" + period.PeriodCode,
            Outcome = outcome,
            Reason = string.IsNullOrWhiteSpace(reason) ? period.Status : reason.Trim(),
            CorrelationId = correlationId,
            AfterReference = period.PeriodCode,
            OccurredAt = now,
        });

    private void EnsureTenant(InstitutionId institutionId)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        if (!string.Equals(
                _tenantContext.CurrentInstitution?.Value,
                institutionId.Value,
                StringComparison.Ordinal))
        {
            throw new LedgerPostingException("institution_context_mismatch");
        }
    }

    private static string LegalEntity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    private static string PeriodCode(int year, int month)
    {
        if (year is < 2000 or > 2100)
        {
            throw new LedgerPostingException("accounting_period_year_invalid");
        }

        if (month is < 1 or > 12)
        {
            throw new LedgerPostingException("accounting_period_month_invalid");
        }

        return year.ToString("0000", CultureInfo.InvariantCulture) + "-"
            + month.ToString("00", CultureInfo.InvariantCulture);
    }

    private static AccountingPeriodResult ToResult(AccountingPeriodRecord period, bool replayed) =>
        new(
            period.InstitutionId,
            period.LegalEntityCode,
            period.PeriodCode,
            period.Status,
            PostingDateGuard.ReadAllowList(period.AllowedEventCodesJson),
            period.Version,
            period.ClosedDebitTotal,
            period.ClosedCreditTotal,
            period.CloseSummaryHash,
            period.ClosedAt,
            period.ReopenExpiresAt,
            period.ReclosedAt,
            period.RecloseReconciliationReference,
            period.LockedAt,
            period.UpdatedAt,
            replayed);
}
