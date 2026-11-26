using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CoreBanking.CoreLedger.Infrastructure.Ledger;

public sealed record AtomicLedgerPostingCommand(
    PostingIdempotencyScope IdempotencyScope,
    FinancialTransaction Transaction,
    JournalEntry Journal,
    LedgerPostingControlContext ControlContext,
    string ActorId,
    string ActorType,
    string CorrelationId,
    string? CausationId = null,
    LedgerHoldCapture? HoldCapture = null,
    LedgerReversalContext? Reversal = null,
    DateAuthority? DateAuthority = null);

public sealed record LedgerHoldCapture(string HoldId, decimal Amount, long ExpectedVersion);

public sealed record LedgerReversalContext(
    string OriginalFinancialTransactionId,
    string FinancialReversalId,
    string ReasonCode,
    string EvidenceReference,
    DateOnly CurrentBusinessDate,
    ReversalFinalityEvidence FinalityEvidence,
    ReversalApprovalEvidence? Approval = null);

public sealed record LedgerPostingControlContext(
    string ChannelCode,
    string TransactionTypeCode,
    IReadOnlyDictionary<string, long> KnownControlVersions);

public sealed record LedgerPostingResult(
    string FinancialTransactionId,
    string TransactionReference,
    string JournalEntryId,
    string JournalReference,
    DateTimeOffset PostedAt,
    bool Replayed);

/// <summary>
/// Commits one single-currency posting and all authoritative evidence in one PostgreSQL transaction.
/// It is intentionally not exposed as an HTTP endpoint until the command authorisation contract is
/// implemented. Decisions: ADR-0005 and P1-E05-D05..D08.
/// </summary>
public sealed class AtomicLedgerPostingService
{
    private const string EventType = "financial_transaction_posted";
    private const string Producer = "core-ledger";
    private const int SchemaVersion = 1;

    private readonly CoreLedgerDbContext _context;
    private readonly FinancialIdempotencyService _idempotency;
    private readonly IAccountControlService _accountControls;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public AtomicLedgerPostingService(
        CoreLedgerDbContext context,
        FinancialIdempotencyService idempotency,
        IAccountControlService accountControls,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _accountControls = accountControls ?? throw new ArgumentNullException(nameof(accountControls));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<LedgerPostingResult> PostAsync(
        AtomicLedgerPostingCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);
        await using IDbContextTransaction databaseTransaction = await _context.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        FinancialIdempotencyClaim claim = await _idempotency
            .ClaimAsync(command.IdempotencyScope, command.Transaction.RequestHash, cancellationToken)
            .ConfigureAwait(false);
        if (claim.Kind == FinancialIdempotencyClaimKind.Replayed)
        {
            if (string.IsNullOrWhiteSpace(claim.Result))
            {
                throw new LedgerPostingException("financial_idempotency_outcome_invalid");
            }

            LedgerPostingResult original = JsonSerializer.Deserialize<LedgerPostingResult>(claim.Result)
                ?? throw new LedgerPostingException("financial_idempotency_outcome_invalid");
            await databaseTransaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return original with { Replayed = true };
        }

        if (claim.Kind == FinancialIdempotencyClaimKind.Conflict)
        {
            throw new LedgerPostingException("ledger_idempotency_conflict");
        }

        if (claim.Kind != FinancialIdempotencyClaimKind.Claimed)
        {
            throw new LedgerPostingException("financial_idempotency_in_progress");
        }

        PreparedReversal? reversal = await PrepareReversalAsync(command, cancellationToken).ConfigureAwait(false);

        PostingDateDecision dateDecision = await PostingDateGuard.EnsureAsync(
            _context,
            command.Transaction.InstitutionId,
            command.Journal.Scope.LegalEntityCode,
            command.Transaction.Dates,
            command.Transaction.EventCode,
            command.DateAuthority,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);

        string[] accountIds = command.Journal.Lines
            .Select(line => line.LedgerAccountId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        IReadOnlyDictionary<string, LockedPostingAccount> accounts = await LockAccountsAndBalancesAsync(
            command.Transaction.InstitutionId,
            accountIds,
            command.Journal.Scope,
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset postedAt = _timeProvider.GetUtcNow();
        PreparedHoldCapture? holdCapture = await LockHoldCaptureAsync(command, accounts, postedAt, cancellationToken)
            .ConfigureAwait(false);
        await ValidateControlsAndAvailableFundsAsync(command, accounts, holdCapture, postedAt, cancellationToken)
            .ConfigureAwait(false);

        command.Transaction.MarkPosted([command.Journal]);
        if (holdCapture is not null)
        {
            ApplyHoldCapture(command, holdCapture, postedAt);
        }

        AddPostingRecords(command.Transaction, command.Journal, command.ActorId, postedAt);
        if (reversal is not null)
        {
            AddReversalRecord(command, reversal, postedAt);
        }

        AddMovements(command, accounts.ToDictionary(pair => pair.Key, pair => pair.Value.Balance, StringComparer.Ordinal), postedAt);
        if (dateDecision.RequiresRecalculationHandoff)
        {
            AddRecalculationHandoff(command, dateDecision, postedAt);
        }

        LedgerPostingResult result = new(
            command.Transaction.Id,
            command.Transaction.TransactionReference.Value,
            command.Journal.JournalEntryId,
            command.Journal.JournalReference.Value,
            postedAt,
            false);
        string outcome = JsonSerializer.Serialize(result);
        await _idempotency.StageOutcomeAsync(
            command.IdempotencyScope,
            command.Transaction.RequestHash,
            outcome,
            cancellationToken).ConfigureAwait(false);
        AddAudit(command, postedAt);
        AddOutbox(command, postedAt);
        if (reversal is not null)
        {
            AddReversalOutbox(command, reversal, postedAt);
        }

        // Once the authoritative rows are staged, cancellation must not turn a successful commit into
        // a reported failure. The durable idempotency outcome is the recovery/status boundary.
        await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        await _context.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE", CancellationToken.None)
            .ConfigureAwait(false);
        await databaseTransaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    private async Task<PreparedReversal?> PrepareReversalAsync(
        AtomicLedgerPostingCommand command,
        CancellationToken cancellationToken)
    {
        LedgerReversalContext? reversal = command.Reversal;
        if (reversal is null)
        {
            return null;
        }

        if (command.HoldCapture is not null)
        {
            throw new LedgerPostingException("financial_reversal_hold_capture_not_allowed");
        }

        if (command.Transaction.Dates.BusinessDate != reversal.CurrentBusinessDate
            || command.Transaction.Dates.PostingDate != reversal.CurrentBusinessDate)
        {
            throw new LedgerPostingException("financial_reversal_posting_date_invalid");
        }

        FinancialTransactionRecord originalTransaction = await _context.FinancialTransactions
            .FromSqlRaw(
                "SELECT * FROM ledger.financial_transactions WHERE institution_id = {0} AND financial_transaction_id = {1} FOR UPDATE",
                command.Transaction.InstitutionId,
                reversal.OriginalFinancialTransactionId.Trim())
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LedgerPostingException("financial_reversal_original_not_found");
        if (!string.Equals(originalTransaction.Status, "posted", StringComparison.Ordinal))
        {
            throw new LedgerPostingException("financial_reversal_original_not_final");
        }

        if (await _context.FinancialReversals.AnyAsync(
                record => record.ReversalFinancialTransactionId == originalTransaction.FinancialTransactionId,
                cancellationToken).ConfigureAwait(false))
        {
            throw new LedgerPostingException("financial_reversal_of_reversal_not_allowed");
        }

        if (await _context.FinancialReversals.AnyAsync(
                record => record.OriginalFinancialTransactionId == originalTransaction.FinancialTransactionId,
                cancellationToken).ConfigureAwait(false))
        {
            throw new LedgerPostingException("financial_transaction_already_reversed");
        }

        JournalEntryRecord originalJournalRecord = await _context.JournalEntries
            .AsNoTracking()
            .SingleAsync(
                record => record.FinancialTransactionId == originalTransaction.FinancialTransactionId,
                cancellationToken)
            .ConfigureAwait(false);
        JournalLineRecord[] originalLineRecords = await _context.JournalLines
            .AsNoTracking()
            .Where(record => record.JournalEntryId == originalJournalRecord.JournalEntryId)
            .OrderBy(record => record.Sequence)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        JournalEntry originalJournal = RestoreJournal(originalJournalRecord, originalLineRecords);
        FinancialReversalPolicy.ValidateFull(
            originalJournal,
            command.Journal,
            reversal.ReasonCode,
            reversal.EvidenceReference,
            command.Transaction.RequestHash,
            reversal.FinalityEvidence,
            reversal.Approval,
            _timeProvider.GetUtcNow());

        return new PreparedReversal(originalTransaction, originalJournalRecord, reversal);
    }

    private async Task<IReadOnlyDictionary<string, LockedPostingAccount>> LockAccountsAndBalancesAsync(
        string institutionId,
        IReadOnlyList<string> accountIds,
        AccountingScope scope,
        CancellationToken cancellationToken)
    {
        Dictionary<string, LockedPostingAccount> accounts = new(StringComparer.Ordinal);
        foreach (string accountId in accountIds)
        {
            LedgerAccountRecord account = await _context.LedgerAccounts
                .FromSqlInterpolated($"SELECT * FROM account.ledger_accounts WHERE institution_id = {institutionId} AND ledger_account_id = {accountId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new LedgerPostingException("ledger_account_not_found");
            if (!string.Equals(account.Status, "initialised", StringComparison.Ordinal)
                || !string.Equals(account.LegalEntityCode, scope.LegalEntityCode, StringComparison.Ordinal)
                || !string.Equals(account.CurrencyCode, scope.Currency.Value, StringComparison.Ordinal))
            {
                throw new LedgerPostingException("ledger_account_not_postable");
            }

            LedgerBalanceRecord balance = await _context.LedgerBalances
                .FromSqlInterpolated($"SELECT * FROM ledger.account_balances WHERE institution_id = {institutionId} AND ledger_account_id = {accountId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new LedgerPostingException("ledger_balance_not_found");

            AccountRecord customerAccount = (await _context.Accounts
                .FromSqlRaw(
                    "SELECT * FROM account.accounts WHERE institution_id = {0} AND account_id = {1} FOR SHARE",
                    institutionId,
                    account.AccountId)
                .IgnoreQueryFilters()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false))
                .SingleOrDefault()
                ?? throw new LedgerPostingException("account_not_found");
            accounts.Add(accountId, new LockedPostingAccount(account, customerAccount, balance));
        }

        return accounts;
    }

    private async Task ValidateControlsAndAvailableFundsAsync(
        AtomicLedgerPostingCommand command,
        IReadOnlyDictionary<string, LockedPostingAccount> accounts,
        PreparedHoldCapture? holdCapture,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        foreach (IGrouping<(string LedgerAccountId, string PostingDirection), JournalLine> group in command.Journal.Lines
                     .GroupBy(line =>
                     {
                         LedgerBalanceRecord balance = accounts[line.LedgerAccountId].Balance;
                         DebitCreditIndicator normalSide = ParseDirection(balance.NormalSide);
                         string postingDirection = line.Direction == normalSide ? "credit" : "debit";
                         return (LedgerAccountId: line.LedgerAccountId, PostingDirection: postingDirection);
                     })
                     .OrderBy(group => group.Key.LedgerAccountId, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.PostingDirection, StringComparer.Ordinal))
        {
            if (!command.ControlContext.KnownControlVersions.TryGetValue(group.Key.LedgerAccountId, out long knownVersion))
            {
                throw new LedgerPostingException("control_version_required");
            }

            LockedPostingAccount account = accounts[group.Key.LedgerAccountId];
            AccountControlEvaluationResult decision = await _accountControls.EvaluateAsync(
                new EvaluateAccountControlsCommand(
                    InstitutionId.From(command.Transaction.InstitutionId),
                    account.CustomerAccount.AccountId,
                    command.Transaction.Id,
                    group.Key.PostingDirection,
                    command.ControlContext.TransactionTypeCode,
                    command.ControlContext.ChannelCode,
                    new MonetaryAmountView(group.Sum(line => line.Amount.Amount), command.Journal.Scope.Currency.Value),
                    knownVersion,
                    evaluatedAt),
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(decision.Outcome, AccountControlOutcomes.Allow, StringComparison.Ordinal))
            {
                string reason = decision.ReasonCodes.FirstOrDefault() ?? "account_control_denied";
                throw new LedgerPostingException(
                    string.Equals(reason, AccountControlOutcomes.VersionStaleCode, StringComparison.Ordinal)
                        ? AccountControlOutcomes.VersionStaleCode
                        : reason.StartsWith(AccountControlOutcomes.ApplicableControlCode, StringComparison.Ordinal)
                            ? "account_" + reason
                            : "account_control_denied");
            }
        }

        foreach ((string ledgerAccountId, decimal reduction) in command.Journal.Lines
                     .Where(line => line.Direction != ParseDirection(accounts[line.LedgerAccountId].Balance.NormalSide))
                     .GroupBy(line => line.LedgerAccountId, StringComparer.Ordinal)
                     .Select(group => (group.Key, group.Sum(line => line.Amount.Amount))))
        {
            LockedPostingAccount account = accounts[ledgerAccountId];
            decimal outstandingLiens = await _context.AccountLiens
                .AsNoTracking()
                .Where(record => record.AccountId == account.CustomerAccount.AccountId
                    && record.Status == "active"
                    && record.EffectiveFrom <= evaluatedAt
                    && (record.EffectiveTo == null || evaluatedAt < record.EffectiveTo))
                .SumAsync(record => (decimal?)record.OutstandingAmount, cancellationToken)
                .ConfigureAwait(false) ?? 0m;
            LedgerBalance balance = Restore(account.Balance);
            decimal reservedCapture = holdCapture is not null
                && string.Equals(holdCapture.Record.LedgerAccountId, ledgerAccountId, StringComparison.Ordinal)
                    ? holdCapture.Amount
                    : 0m;
            if (reservedCapture > reduction)
            {
                throw new LedgerPostingException("ledger_hold_capture_posting_mismatch");
            }

            if (balance.Available(outstandingLiens).Available < reduction - reservedCapture)
            {
                throw new LedgerPostingException("insufficient_available_funds");
            }
        }
    }

    private async Task<PreparedHoldCapture?> LockHoldCaptureAsync(
        AtomicLedgerPostingCommand command,
        IReadOnlyDictionary<string, LockedPostingAccount> accounts,
        DateTimeOffset postedAt,
        CancellationToken cancellationToken)
    {
        if (command.HoldCapture is null)
        {
            return null;
        }

        LedgerHoldRecord record = await _context.LedgerHolds
            .FromSqlRaw(
                "SELECT * FROM ledger.holds WHERE institution_id = {0} AND hold_id = {1} FOR UPDATE",
                command.Transaction.InstitutionId,
                command.HoldCapture.HoldId.Trim())
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LedgerPostingException("ledger_hold_not_found");
        if (!accounts.ContainsKey(record.LedgerAccountId))
        {
            throw new LedgerPostingException("ledger_hold_capture_posting_mismatch");
        }

        if (record.Version != command.HoldCapture.ExpectedVersion)
        {
            throw new LedgerPostingException("ledger_hold_version_stale");
        }

        LedgerHold hold = LedgerHoldService.Restore(record);
        if (!string.Equals(record.CurrencyCode, command.Journal.Scope.Currency.Value, StringComparison.Ordinal))
        {
            throw new LedgerPostingException("ledger_hold_currency_mismatch");
        }

        if (command.HoldCapture.Amount <= 0m || command.HoldCapture.Amount > hold.RemainingAmount)
        {
            throw new LedgerPostingException("ledger_hold_capture_exceeds_remaining");
        }

        if (postedAt >= hold.ExpiresAt)
        {
            throw new LedgerPostingException("ledger_hold_expired");
        }

        return new PreparedHoldCapture(record, hold, command.HoldCapture.Amount);
    }

    private void ApplyHoldCapture(
        AtomicLedgerPostingCommand command,
        PreparedHoldCapture capture,
        DateTimeOffset postedAt)
    {
        capture.Hold.Capture(capture.Amount, postedAt);
        LedgerHoldService.ApplyHold(capture.Record, capture.Hold, postedAt);
        LedgerBalanceRecord balanceRecord = _context.LedgerBalances.Local.Single(
            record => record.LedgerAccountId == capture.Record.LedgerAccountId);
        LedgerBalance balance = Restore(balanceRecord);
        balance.ReduceHold(capture.Amount);
        LedgerHoldService.ApplyBalance(balanceRecord, balance, postedAt);
        _context.LedgerHoldHistory.Add(new LedgerHoldHistoryRecord
        {
            InstitutionId = capture.Record.InstitutionId,
            HistoryId = Guid.NewGuid().ToString("N"),
            HoldId = capture.Record.HoldId,
            Sequence = checked((int)capture.Record.Version),
            Transition = "captured",
            Amount = capture.Amount,
            RemainingAfter = capture.Record.RemainingAmount,
            FinancialTransactionId = command.Transaction.Id,
            ReasonCode = command.Transaction.EventCode,
            ActorId = command.ActorId,
            OccurredAt = postedAt,
        });
        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = capture.Record.InstitutionId,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = command.ActorId,
            ActorType = command.ActorType,
            Permission = "ledger.hold.capture",
            Action = "ledger.hold.capture",
            ResourceType = "ledger.hold",
            ResourceId = capture.Record.HoldId,
            Outcome = "succeeded",
            Reason = command.Transaction.Id,
            CorrelationId = command.CorrelationId,
            AfterReference = command.Transaction.TransactionReference.Value,
            OccurredAt = postedAt,
        });
        AddHoldOutbox(command, capture.Record, postedAt);
    }

    private void AddHoldOutbox(
        AtomicLedgerPostingCommand command,
        LedgerHoldRecord record,
        DateTimeOffset postedAt)
    {
        string eventId = Guid.NewGuid().ToString("N");
        LedgerHoldChangedV1 payload = new(
            record.HoldId,
            record.LedgerAccountId,
            record.SourceService,
            record.SourceReference,
            record.OriginalAmount,
            record.RemainingAmount,
            record.CurrencyCode,
            record.Status,
            record.Version,
            record.ExpiresAt,
            postedAt);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = "ledger_hold_changed",
            SchemaVersion = 1,
            OccurredAt = postedAt,
            InstitutionId = record.InstitutionId,
            CorrelationId = command.CorrelationId,
            CausationId = command.CausationId,
            Producer = Producer,
            BusinessReference = record.HoldId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = record.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = "ledger_hold_changed",
            SchemaVersion = 1,
            Topic = EventContract.TopicFor(Producer, "ledger-holds", 1),
            BusinessReference = record.HoldId,
            CorrelationId = command.CorrelationId,
            CausationId = command.CausationId,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = postedAt,
            CreatedAt = postedAt,
        });
    }

    private void AddMovements(
        AtomicLedgerPostingCommand command,
        Dictionary<string, LedgerBalanceRecord> balanceRecords,
        DateTimeOffset postedAt)
    {
        Dictionary<string, LedgerBalance> balances = balanceRecords.ToDictionary(
            pair => pair.Key,
            pair => Restore(pair.Value),
            StringComparer.Ordinal);

        foreach (JournalLine line in command.Journal.Lines.OrderBy(line => line.Sequence))
        {
            LedgerBalance balance = balances[line.LedgerAccountId];
            LedgerMovement movement = balance.Apply(line, Guid.NewGuid().ToString("N"), postedAt);
            LedgerBalanceRecord record = balanceRecords[line.LedgerAccountId];
            record.DebitTotal = balance.DebitTotal;
            record.CreditTotal = balance.CreditTotal;
            record.NetBalance = balance.NetBalance;
            record.Version = balance.Version;
            record.AsOf = postedAt;
            _context.LedgerMovements.Add(new LedgerMovementRecord
            {
                InstitutionId = command.Transaction.InstitutionId,
                MovementId = movement.MovementId,
                JournalLineId = movement.JournalLineId,
                LedgerAccountId = movement.LedgerAccountId,
                Direction = movement.Direction.ToString().ToLowerInvariant(),
                Amount = movement.Amount.Amount,
                SignedAmount = movement.Direction == DebitCreditIndicator.Debit
                    ? movement.Amount.Amount
                    : -movement.Amount.Amount,
                VisibleBalanceBefore = movement.VisibleBalanceBefore,
                VisibleBalanceAfter = movement.VisibleBalanceAfter,
                DebitTotalAfter = movement.DebitTotalAfter,
                CreditTotalAfter = movement.CreditTotalAfter,
                BalanceVersion = movement.BalanceVersion,
                OccurredAt = movement.OccurredAt,
            });
        }
    }

    private void AddPostingRecords(
        FinancialTransaction transaction,
        JournalEntry journal,
        string actorId,
        DateTimeOffset postedAt)
    {
        _context.FinancialTransactions.Add(new FinancialTransactionRecord
        {
            InstitutionId = transaction.InstitutionId,
            FinancialTransactionId = transaction.Id,
            TransactionReference = transaction.TransactionReference.Value,
            SourceService = transaction.SourceService,
            SourceReference = transaction.SourceReference.Value,
            EventCode = transaction.EventCode,
            RequestHash = transaction.RequestHash,
            Status = "posted",
            TransactionTimestamp = transaction.Dates.TransactionTimestamp,
            BusinessDate = transaction.Dates.BusinessDate,
            PostingDate = transaction.Dates.PostingDate,
            ValueDate = transaction.Dates.ValueDate,
            Version = 1,
            CreatedAt = postedAt,
            PostedAt = postedAt,
        });
        _context.FinancialTransactionStatusHistory.Add(new FinancialTransactionStatusHistoryRecord
        {
            InstitutionId = transaction.InstitutionId,
            HistoryId = Guid.NewGuid().ToString("N"),
            FinancialTransactionId = transaction.Id,
            Sequence = 1,
            Status = "posted",
            ReasonCode = "posting_committed",
            ActorId = actorId,
            OccurredAt = postedAt,
        });
        _context.JournalEntries.Add(new JournalEntryRecord
        {
            InstitutionId = transaction.InstitutionId,
            JournalEntryId = journal.JournalEntryId,
            FinancialTransactionId = transaction.Id,
            JournalReference = journal.JournalReference.Value,
            MappingVersionId = journal.MappingVersionId,
            LegalEntityCode = journal.Scope.LegalEntityCode,
            CurrencyCode = journal.Scope.Currency.Value,
            Status = "posted",
            TotalDebits = journal.TotalDebits,
            TotalCredits = journal.TotalCredits,
            TransactionTimestamp = journal.Dates.TransactionTimestamp,
            BusinessDate = journal.Dates.BusinessDate,
            PostingDate = journal.Dates.PostingDate,
            ValueDate = journal.Dates.ValueDate,
            CreatedAt = postedAt,
        });
        _context.JournalLines.AddRange(journal.Lines.Select(line => new JournalLineRecord
        {
            InstitutionId = transaction.InstitutionId,
            JournalLineId = line.LineId,
            JournalEntryId = journal.JournalEntryId,
            Sequence = line.Sequence,
            LedgerAccountId = line.LedgerAccountId,
            LegalEntityCode = line.Scope.LegalEntityCode,
            CurrencyCode = line.Scope.Currency.Value,
            Direction = line.Direction.ToString().ToLowerInvariant(),
            ComponentType = line.Component.ToString().ToLowerInvariant(),
            Amount = line.Amount.Amount,
            ReasonCode = line.ReasonCode,
            CreatedAt = postedAt,
        }));
    }

    private void AddReversalRecord(
        AtomicLedgerPostingCommand command,
        PreparedReversal prepared,
        DateTimeOffset postedAt)
    {
        LedgerReversalContext reversal = prepared.Context;
        _context.FinancialReversals.Add(new FinancialReversalRecord
        {
            InstitutionId = command.Transaction.InstitutionId,
            FinancialReversalId = reversal.FinancialReversalId.Trim(),
            OriginalFinancialTransactionId = prepared.OriginalTransaction.FinancialTransactionId,
            ReversalFinancialTransactionId = command.Transaction.Id,
            OriginalJournalEntryId = prepared.OriginalJournal.JournalEntryId,
            ReversalJournalEntryId = command.Journal.JournalEntryId,
            MappingVersionId = command.Journal.MappingVersionId,
            CurrencyCode = command.Journal.Scope.Currency.Value,
            ReversedAmount = command.Journal.TotalDebits,
            RemainingAfter = 0m,
            ReasonCode = reversal.ReasonCode.Trim(),
            EvidenceReference = reversal.EvidenceReference.Trim(),
            WorkflowDecisionId = reversal.Approval?.WorkflowDecisionId.Trim(),
            MakerActorId = reversal.Approval?.MakerActorId.Trim(),
            ApproverActorId = reversal.Approval?.ApproverActorId.Trim(),
            OriginalValueDate = prepared.OriginalJournal.ValueDate,
            ReversalValueDate = command.Journal.Dates.ValueDate,
            OccurredAt = postedAt,
        });
    }

    private void AddRecalculationHandoff(
        AtomicLedgerPostingCommand command,
        PostingDateDecision decision,
        DateTimeOffset postedAt)
    {
        DateAuthority authority = command.DateAuthority
            ?? throw new LedgerPostingException("backdate_authority_required");
        _context.RecalculationHandoffs.Add(new RecalculationHandoffRecord
        {
            InstitutionId = command.Transaction.InstitutionId,
            HandoffId = Guid.NewGuid().ToString("N"),
            LegalEntityCode = command.Journal.Scope.LegalEntityCode,
            PeriodCode = command.Transaction.Dates.PostingDate.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
            FinancialTransactionId = command.Transaction.Id,
            ValueDate = command.Transaction.Dates.ValueDate,
            AgeDays = decision.AgeDays,
            ReasonCode = authority.ReasonCode.Trim(),
            EvidenceReference = authority.EvidenceReference.Trim(),
            ApprovalReference = authority.ApprovalReference.Trim(),
            RecordedAt = postedAt,
        });
    }

    private void AddAudit(AtomicLedgerPostingCommand command, DateTimeOffset postedAt) =>
        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = command.Transaction.InstitutionId,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = command.ActorId,
            ActorType = command.ActorType,
            Permission = command.Reversal is null ? "ledger.transaction.post" : "ledger.transaction.reverse",
            Action = command.Reversal is null ? "ledger.transaction.post" : "ledger.transaction.reverse",
            ResourceType = "ledger.financial_transaction",
            ResourceId = command.Transaction.Id,
            Outcome = "succeeded",
            Reason = command.Reversal?.ReasonCode ?? command.Transaction.EventCode,
            CorrelationId = command.CorrelationId,
            AfterReference = command.Transaction.TransactionReference.Value,
            OccurredAt = postedAt,
        });

    private void AddReversalOutbox(
        AtomicLedgerPostingCommand command,
        PreparedReversal prepared,
        DateTimeOffset postedAt)
    {
        string eventId = Guid.NewGuid().ToString("N");
        FinancialTransactionReversedV1 payload = new(
            prepared.OriginalTransaction.FinancialTransactionId,
            prepared.OriginalTransaction.TransactionReference,
            command.Transaction.Id,
            command.Transaction.TransactionReference.Value,
            command.Journal.JournalEntryId,
            command.Journal.JournalReference.Value,
            command.Journal.Scope.Currency.Value,
            command.Journal.TotalDebits,
            prepared.Context.ReasonCode,
            prepared.Context.EvidenceReference,
            command.Transaction.Dates.PostingDate,
            postedAt);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = "financial_transaction_reversed",
            SchemaVersion = 1,
            OccurredAt = postedAt,
            InstitutionId = command.Transaction.InstitutionId,
            CorrelationId = command.CorrelationId,
            CausationId = command.CausationId,
            Producer = Producer,
            BusinessReference = prepared.OriginalTransaction.FinancialTransactionId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = command.Transaction.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = "financial_transaction_reversed",
            SchemaVersion = 1,
            Topic = EventContract.TopicFor(Producer, "financial-reversals", 1),
            BusinessReference = prepared.OriginalTransaction.FinancialTransactionId,
            CorrelationId = command.CorrelationId,
            CausationId = command.CausationId,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = postedAt,
            CreatedAt = postedAt,
        });
    }

    private void AddOutbox(AtomicLedgerPostingCommand command, DateTimeOffset postedAt)
    {
        string eventId = Guid.NewGuid().ToString("N");
        string payload = EventContract.Serialize(new FinancialTransactionPostedV1(
            command.Transaction.Id,
            command.Transaction.TransactionReference.Value,
            command.Journal.JournalEntryId,
            command.Journal.JournalReference.Value,
            command.Journal.Scope.LegalEntityCode,
            command.Journal.Scope.Currency.Value,
            command.Journal.TotalDebits,
            command.Transaction.EventCode,
            command.Transaction.Dates.PostingDate,
            postedAt));
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = EventType,
            SchemaVersion = SchemaVersion,
            OccurredAt = postedAt,
            InstitutionId = command.Transaction.InstitutionId,
            CorrelationId = command.CorrelationId,
            CausationId = command.CausationId,
            Producer = Producer,
            BusinessReference = command.Transaction.Id,
            Payload = payload,
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = command.Transaction.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = EventType,
            SchemaVersion = SchemaVersion,
            Topic = EventContract.TopicFor(Producer, "financial-postings", SchemaVersion),
            BusinessReference = command.Transaction.Id,
            CorrelationId = command.CorrelationId,
            CausationId = command.CausationId,
            TraceParent = System.Diagnostics.Activity.Current?.Id,
            TraceState = System.Diagnostics.Activity.Current?.TraceStateString,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = postedAt,
            CreatedAt = postedAt,
        });
    }

    private void Validate(AtomicLedgerPostingCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.IdempotencyScope);
        ArgumentNullException.ThrowIfNull(command.Transaction);
        ArgumentNullException.ThrowIfNull(command.Journal);
        ArgumentNullException.ThrowIfNull(command.ControlContext);
        ArgumentNullException.ThrowIfNull(command.ControlContext.KnownControlVersions);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ControlContext.ChannelCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ControlContext.TransactionTypeCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ActorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ActorType);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        string? institution = _tenantContext.CurrentInstitution?.Value;
        if (!string.Equals(institution, command.Transaction.InstitutionId, StringComparison.Ordinal)
            || !string.Equals(institution, command.Journal.Scope.InstitutionId, StringComparison.Ordinal)
            || !string.Equals(institution, command.IdempotencyScope.InstitutionId.Value, StringComparison.Ordinal))
        {
            throw new LedgerPostingException("institution_context_mismatch");
        }

        if (!string.Equals(command.Journal.FinancialTransactionId, command.Transaction.Id, StringComparison.Ordinal)
            || command.Transaction.Dates != command.Journal.Dates)
        {
            throw new LedgerPostingException("financial_transaction_journal_mismatch");
        }

        if (command.Reversal is not null
            && string.Equals(command.Reversal.OriginalFinancialTransactionId, command.Transaction.Id, StringComparison.Ordinal))
        {
            throw new LedgerPostingException("financial_reversal_transaction_identity_invalid");
        }
    }

    private static DebitCreditIndicator ParseDirection(string value) => value switch
    {
        "debit" => DebitCreditIndicator.Debit,
        "credit" => DebitCreditIndicator.Credit,
        _ => throw new LedgerPostingException("ledger_balance_normal_side_invalid"),
    };

    private static LedgerBalance Restore(LedgerBalanceRecord record) =>
        LedgerBalance.Restore(
            record.LedgerAccountId,
            record.CurrencyCode,
            ParseDirection(record.NormalSide),
            record.DebitTotal,
            record.CreditTotal,
            record.CreditLimit,
            record.HoldRemaining,
            record.PendingAuthorised,
            record.Version);

    private static JournalEntry RestoreJournal(
        JournalEntryRecord journal,
        IReadOnlyCollection<JournalLineRecord> lines)
    {
        AccountingScope scope = new(
            journal.InstitutionId,
            journal.LegalEntityCode,
            CurrencyCode.Parse(journal.CurrencyCode));
        FinancialDates dates = FinancialDates.Create(
            journal.TransactionTimestamp,
            journal.BusinessDate,
            journal.PostingDate,
            journal.ValueDate);
        return JournalEntry.Create(
            journal.JournalEntryId,
            journal.FinancialTransactionId,
            journal.JournalReference,
            journal.MappingVersionId,
            scope,
            dates,
            lines.Select(line => JournalLine.Create(
                line.JournalLineId,
                line.Sequence,
                line.LedgerAccountId,
                ParseDirection(line.Direction),
                ParseComponent(line.ComponentType),
                Money.Create(line.Amount, line.CurrencyCode),
                scope,
                line.ReasonCode)));
    }

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

    private sealed record LockedPostingAccount(
        LedgerAccountRecord LedgerAccount,
        AccountRecord CustomerAccount,
        LedgerBalanceRecord Balance);

    private sealed record PreparedHoldCapture(
        LedgerHoldRecord Record,
        LedgerHold Hold,
        decimal Amount);

    private sealed record PreparedReversal(
        FinancialTransactionRecord OriginalTransaction,
        JournalEntryRecord OriginalJournal,
        LedgerReversalContext Context);
}

public sealed record FinancialTransactionPostedV1(
    string FinancialTransactionId,
    string TransactionReference,
    string JournalEntryId,
    string JournalReference,
    string LegalEntityCode,
    string Currency,
    decimal Amount,
    string EventCode,
    DateOnly PostingDate,
    DateTimeOffset PostedAt);

public sealed record FinancialTransactionReversedV1(
    string OriginalFinancialTransactionId,
    string OriginalTransactionReference,
    string ReversalFinancialTransactionId,
    string ReversalTransactionReference,
    string ReversalJournalEntryId,
    string ReversalJournalReference,
    string Currency,
    decimal Amount,
    string ReasonCode,
    string EvidenceReference,
    DateOnly PostingDate,
    DateTimeOffset PostedAt);
