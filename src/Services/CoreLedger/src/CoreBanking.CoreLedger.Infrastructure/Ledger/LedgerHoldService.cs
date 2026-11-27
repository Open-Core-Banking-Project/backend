using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Application.Ledger;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CoreBanking.CoreLedger.Infrastructure.Ledger;

/// <summary>
/// Owns hold reservation, release and expiry. Every transition locks the authoritative balance,
/// changes only the reserved component, and commits history/audit/outbox/idempotency atomically.
/// Decision: P1-E05-D06.
/// </summary>
public sealed class LedgerHoldService : ILedgerHoldService
{
    private const string Producer = "core-ledger";
    private const string EventType = "ledger_hold_changed";
    private const int SchemaVersion = 1;

    private readonly CoreLedgerDbContext _context;
    private readonly FinancialIdempotencyService _idempotency;
    private readonly IAccountControlService _accountControls;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public LedgerHoldService(
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

    public async Task<LedgerHoldResult> CreateAsync(
        CreateLedgerHoldCommand command,
        CancellationToken cancellationToken)
    {
        ValidateInstitution(command.IdempotencyScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.LedgerAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SourceService);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SourceReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.PurposeCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ChannelCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TransactionTypeCode);

        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        FinancialIdempotencyClaim claim = await ClaimAsync(
            command.IdempotencyScope,
            command.RequestHash,
            cancellationToken).ConfigureAwait(false);
        if (claim.Kind == FinancialIdempotencyClaimKind.Replayed)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return Replay(claim.Result);
        }

        EnsureClaimed(claim);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        (LedgerAccountRecord ledgerAccount, AccountRecord customerAccount, LedgerBalanceRecord balanceRecord) =
            await LockAccountAndBalanceAsync(command.LedgerAccountId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(ledgerAccount.CurrencyCode, command.Amount.Currency.Value, StringComparison.Ordinal))
        {
            throw new LedgerPostingException("ledger_hold_currency_mismatch");
        }

        AccountControlEvaluationResult control = await _accountControls.EvaluateAsync(
            new EvaluateAccountControlsCommand(
                InstitutionId.From(ledgerAccount.InstitutionId),
                customerAccount.AccountId,
                command.SourceReference,
                "debit",
                command.TransactionTypeCode,
                command.ChannelCode,
                new MonetaryAmountView(command.Amount.Amount, command.Amount.Currency.Value),
                command.KnownControlVersion,
                now),
            cancellationToken).ConfigureAwait(false);
        EnsureControlAllowed(control);

        decimal liens = await OutstandingLiensAsync(customerAccount.AccountId, now, cancellationToken).ConfigureAwait(false);
        LedgerBalance balance = Restore(balanceRecord);
        balance.ReserveHold(command.Amount.Amount, liens);
        ApplyBalance(balanceRecord, balance, now);

        LedgerHold hold = LedgerHold.Create(
            Guid.NewGuid().ToString("N"),
            ledgerAccount.LedgerAccountId,
            command.Amount,
            command.ExpiresAt,
            now);
        LedgerHoldRecord record = ToRecord(ledgerAccount.InstitutionId, hold, command, now);
        _context.LedgerHolds.Add(record);
        AddHistory(record, "created", command.Amount.Amount, null, command.PurposeCode, command.ActorId, now);
        AddAudit(record.InstitutionId, command.ActorId, command.ActorType, "ledger.hold.create", record.HoldId,
            command.PurposeCode, command.CorrelationId, now);
        AddOutbox(record, command.CorrelationId, command.CausationId, now);

        LedgerHoldResult result = ToResult(record, now, replayed: false);
        await CompleteAsync(command.IdempotencyScope, command.RequestHash, result, transaction).ConfigureAwait(false);
        return result;
    }

    public Task<LedgerHoldResult> ReleaseAsync(
        ReleaseLedgerHoldCommand command,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            command.IdempotencyScope,
            command.RequestHash,
            command.HoldId,
            command.ExpectedVersion,
            command.Amount,
            command.ReasonCode,
            command.ActorId,
            command.ActorType,
            command.CorrelationId,
            command.CausationId,
            expire: false,
            cancellationToken);

    public Task<LedgerHoldResult> ExpireAsync(
        ExpireLedgerHoldCommand command,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            command.IdempotencyScope,
            command.RequestHash,
            command.HoldId,
            command.ExpectedVersion,
            0m,
            "hold_expired",
            command.ActorId,
            command.ActorType,
            command.CorrelationId,
            command.CausationId,
            expire: true,
            cancellationToken);

    private async Task<LedgerHoldResult> TransitionAsync(
        PostingIdempotencyScope scope,
        string requestHash,
        string holdId,
        long expectedVersion,
        decimal amount,
        string reasonCode,
        string actorId,
        string actorType,
        string correlationId,
        string? causationId,
        bool expire,
        CancellationToken cancellationToken)
    {
        ValidateInstitution(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(holdId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        FinancialIdempotencyClaim claim = await ClaimAsync(scope, requestHash, cancellationToken).ConfigureAwait(false);
        if (claim.Kind == FinancialIdempotencyClaimKind.Replayed)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return Replay(claim.Result);
        }

        EnsureClaimed(claim);
        string ledgerAccountId = await _context.LedgerHolds
            .AsNoTracking()
            .Where(record => record.HoldId == holdId.Trim())
            .Select(record => record.LedgerAccountId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LedgerPostingException("ledger_hold_not_found");
        LedgerBalanceRecord balanceRecord = await LockBalanceAsync(ledgerAccountId, cancellationToken).ConfigureAwait(false);
        LedgerHoldRecord record = await LockHoldAsync(holdId, cancellationToken).ConfigureAwait(false);
        if (record.Version != expectedVersion)
        {
            throw new LedgerPostingException("ledger_hold_version_stale");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        LedgerHold hold = Restore(record);
        decimal reduction;
        string transition;
        if (expire)
        {
            reduction = hold.Expire(now);
            transition = "expired";

            if (reduction == 0m)
            {
                LedgerHoldResult unchanged = ToResult(record, now, replayed: false);
                await CompleteAsync(scope, requestHash, unchanged, transaction).ConfigureAwait(false);
                return unchanged;
            }
        }
        else
        {
            hold.Release(amount, now);
            reduction = amount;
            transition = "released";
        }

        if (reduction > 0m)
        {
            LedgerBalance balance = Restore(balanceRecord);
            balance.ReduceHold(reduction);
            ApplyBalance(balanceRecord, balance, now);
        }

        ApplyHold(record, hold, now);
        AddHistory(record, transition, reduction, null, reasonCode, actorId, now);
        AddAudit(record.InstitutionId, actorId, actorType, expire ? "ledger.hold.expire" : "ledger.hold.release",
            record.HoldId, reasonCode, correlationId, now);
        AddOutbox(record, correlationId, causationId, now);

        LedgerHoldResult result = ToResult(record, now, replayed: false);
        await CompleteAsync(scope, requestHash, result, transaction).ConfigureAwait(false);
        return result;
    }

    private async Task CompleteAsync(
        PostingIdempotencyScope scope,
        string requestHash,
        LedgerHoldResult result,
        IDbContextTransaction transaction)
    {
        await _idempotency.StageOutcomeAsync(scope, requestHash, JsonSerializer.Serialize(result), CancellationToken.None)
            .ConfigureAwait(false);
        await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private Task<FinancialIdempotencyClaim> ClaimAsync(
        PostingIdempotencyScope scope,
        string requestHash,
        CancellationToken cancellationToken) =>
        _idempotency.ClaimAsync(scope, requestHash, cancellationToken);

    private async Task<(LedgerAccountRecord Ledger, AccountRecord Customer, LedgerBalanceRecord Balance)>
        LockAccountAndBalanceAsync(string ledgerAccountId, CancellationToken cancellationToken)
    {
        string institutionId = _tenantContext.CurrentInstitution?.Value
            ?? throw new LedgerPostingException("institution_context_required");
        LedgerAccountRecord ledger = await _context.LedgerAccounts
            .FromSqlRaw(
                "SELECT * FROM account.ledger_accounts WHERE institution_id = {0} AND ledger_account_id = {1} FOR UPDATE",
                institutionId,
                ledgerAccountId.Trim())
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LedgerPostingException("ledger_account_not_found");
        if (!string.Equals(ledger.Status, "initialised", StringComparison.Ordinal))
        {
            throw new LedgerPostingException("ledger_account_not_postable");
        }

        LedgerBalanceRecord balance = await LockBalanceAsync(ledger.LedgerAccountId, cancellationToken).ConfigureAwait(false);
        AccountRecord customer = await _context.Accounts
            .FromSqlRaw(
                "SELECT * FROM account.accounts WHERE institution_id = {0} AND account_id = {1} FOR SHARE",
                institutionId,
                ledger.AccountId)
            .IgnoreQueryFilters()
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        return (ledger, customer, balance);
    }

    private async Task<LedgerBalanceRecord> LockBalanceAsync(
        string ledgerAccountId,
        CancellationToken cancellationToken)
    {
        string institutionId = _tenantContext.CurrentInstitution?.Value
            ?? throw new LedgerPostingException("institution_context_required");
        return await _context.LedgerBalances
            .FromSqlRaw(
                "SELECT * FROM ledger.account_balances WHERE institution_id = {0} AND ledger_account_id = {1} FOR UPDATE",
                institutionId,
                ledgerAccountId.Trim())
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LedgerPostingException("ledger_balance_not_found");
    }

    private async Task<LedgerHoldRecord> LockHoldAsync(string holdId, CancellationToken cancellationToken)
    {
        string institutionId = _tenantContext.CurrentInstitution?.Value
            ?? throw new LedgerPostingException("institution_context_required");
        return await _context.LedgerHolds
            .FromSqlRaw(
                "SELECT * FROM ledger.holds WHERE institution_id = {0} AND hold_id = {1} FOR UPDATE",
                institutionId,
                holdId.Trim())
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LedgerPostingException("ledger_hold_not_found");
    }

    private async Task<decimal> OutstandingLiensAsync(
        string accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await _context.AccountLiens
            .AsNoTracking()
            .Where(record => record.AccountId == accountId
                && record.Status == "active"
                && record.EffectiveFrom <= now
                && (record.EffectiveTo == null || now < record.EffectiveTo))
            .SumAsync(record => (decimal?)record.OutstandingAmount, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

    private void ValidateInstitution(PostingIdempotencyScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!string.Equals(_tenantContext.CurrentInstitution?.Value, scope.InstitutionId.Value, StringComparison.Ordinal))
        {
            throw new LedgerPostingException("institution_context_mismatch");
        }
    }

    private static void EnsureClaimed(FinancialIdempotencyClaim claim)
    {
        if (claim.Kind == FinancialIdempotencyClaimKind.Conflict)
        {
            throw new LedgerPostingException("ledger_idempotency_conflict");
        }

        if (claim.Kind != FinancialIdempotencyClaimKind.Claimed)
        {
            throw new LedgerPostingException("financial_idempotency_in_progress");
        }
    }

    private static void EnsureControlAllowed(AccountControlEvaluationResult control)
    {
        if (string.Equals(control.Outcome, AccountControlOutcomes.Allow, StringComparison.Ordinal))
        {
            return;
        }

        string reason = control.ReasonCodes.FirstOrDefault() ?? "account_control_denied";
        throw new LedgerPostingException(
            string.Equals(reason, AccountControlOutcomes.VersionStaleCode, StringComparison.Ordinal)
                ? reason
                : reason.StartsWith(AccountControlOutcomes.ApplicableControlCode, StringComparison.Ordinal)
                    ? "account_" + reason
                    : "account_control_denied");
    }

    private static LedgerHoldRecord ToRecord(
        string institutionId,
        LedgerHold hold,
        CreateLedgerHoldCommand command,
        DateTimeOffset now) =>
        new()
        {
            InstitutionId = institutionId,
            HoldId = hold.HoldId,
            LedgerAccountId = hold.LedgerAccountId,
            SourceService = command.SourceService.Trim(),
            SourceReference = command.SourceReference.Trim(),
            PurposeCode = command.PurposeCode.Trim(),
            CurrencyCode = hold.OriginalAmount.Currency.Value,
            OriginalAmount = hold.OriginalAmount.Amount,
            RemainingAmount = hold.RemainingAmount,
            CapturedAmount = hold.CapturedAmount,
            ReleasedAmount = hold.ReleasedAmount,
            Status = Status(hold.Status),
            ExpiresAt = hold.ExpiresAt,
            Version = hold.Version,
            CreatedAt = now,
            UpdatedAt = now,
        };

    internal static LedgerHold Restore(LedgerHoldRecord record) =>
        LedgerHold.Restore(
            record.HoldId,
            record.LedgerAccountId,
            Money.Create(record.OriginalAmount, record.CurrencyCode),
            record.RemainingAmount,
            record.CapturedAmount,
            record.ReleasedAmount,
            ParseStatus(record.Status),
            record.ExpiresAt,
            record.Version);

    internal static void ApplyHold(LedgerHoldRecord record, LedgerHold hold, DateTimeOffset now)
    {
        record.RemainingAmount = hold.RemainingAmount;
        record.CapturedAmount = hold.CapturedAmount;
        record.ReleasedAmount = hold.ReleasedAmount;
        record.Status = Status(hold.Status);
        record.Version = hold.Version;
        record.UpdatedAt = now;
    }

    internal static LedgerBalance Restore(LedgerBalanceRecord record) =>
        LedgerBalance.Restore(
            record.LedgerAccountId,
            record.CurrencyCode,
            record.NormalSide == "debit" ? DebitCreditIndicator.Debit : DebitCreditIndicator.Credit,
            record.DebitTotal,
            record.CreditTotal,
            record.CreditLimit,
            record.HoldRemaining,
            record.PendingAuthorised,
            record.Version);

    internal static void ApplyBalance(LedgerBalanceRecord record, LedgerBalance balance, DateTimeOffset now)
    {
        record.HoldRemaining = balance.HoldRemaining;
        record.Version = balance.Version;
        record.AsOf = now;
    }

    internal void AddHistory(
        LedgerHoldRecord record,
        string transition,
        decimal amount,
        string? financialTransactionId,
        string reasonCode,
        string actorId,
        DateTimeOffset now) =>
        _context.LedgerHoldHistory.Add(new LedgerHoldHistoryRecord
        {
            InstitutionId = record.InstitutionId,
            HistoryId = Guid.NewGuid().ToString("N"),
            HoldId = record.HoldId,
            Sequence = checked((int)record.Version),
            Transition = transition,
            Amount = amount,
            RemainingAfter = record.RemainingAmount,
            FinancialTransactionId = financialTransactionId,
            ReasonCode = reasonCode.Trim(),
            ActorId = actorId.Trim(),
            OccurredAt = now,
        });

    internal void AddOutbox(
        LedgerHoldRecord record,
        string correlationId,
        string? causationId,
        DateTimeOffset now)
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
            now);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = EventType,
            SchemaVersion = SchemaVersion,
            OccurredAt = now,
            InstitutionId = record.InstitutionId,
            CorrelationId = correlationId,
            CausationId = causationId,
            Producer = Producer,
            BusinessReference = record.HoldId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = record.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = EventType,
            SchemaVersion = SchemaVersion,
            Topic = EventContract.TopicFor(Producer, "ledger-holds", SchemaVersion),
            BusinessReference = record.HoldId,
            CorrelationId = correlationId,
            CausationId = causationId,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });
    }

    private void AddAudit(
        string institutionId,
        string actorId,
        string actorType,
        string action,
        string holdId,
        string reason,
        string correlationId,
        DateTimeOffset now) =>
        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = institutionId,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = actorId.Trim(),
            ActorType = actorType.Trim(),
            Permission = action,
            Action = action,
            ResourceType = "ledger.hold",
            ResourceId = holdId,
            Outcome = "succeeded",
            Reason = reason.Trim(),
            CorrelationId = correlationId.Trim(),
            OccurredAt = now,
        });

    private static LedgerHoldResult Replay(string? value)
    {
        LedgerHoldResult result = string.IsNullOrWhiteSpace(value)
            ? throw new LedgerPostingException("financial_idempotency_outcome_invalid")
            : JsonSerializer.Deserialize<LedgerHoldResult>(value)
                ?? throw new LedgerPostingException("financial_idempotency_outcome_invalid");
        return result with { Replayed = true };
    }

    internal static LedgerHoldResult ToResult(LedgerHoldRecord record, DateTimeOffset now, bool replayed) =>
        new(
            record.HoldId,
            record.LedgerAccountId,
            record.OriginalAmount,
            record.RemainingAmount,
            record.CapturedAmount,
            record.ReleasedAmount,
            record.CurrencyCode,
            record.Status,
            record.Version,
            record.ExpiresAt,
            now,
            replayed);

    internal static string Status(LedgerHoldStatus status) => status switch
    {
        LedgerHoldStatus.Active => "active",
        LedgerHoldStatus.PartiallyCaptured => "partially_captured",
        LedgerHoldStatus.Captured => "captured",
        LedgerHoldStatus.Released => "released",
        LedgerHoldStatus.Expired => "expired",
        _ => throw new LedgerPostingException("ledger_hold_status_invalid"),
    };

    internal static LedgerHoldStatus ParseStatus(string status) => status switch
    {
        "active" => LedgerHoldStatus.Active,
        "partially_captured" => LedgerHoldStatus.PartiallyCaptured,
        "captured" => LedgerHoldStatus.Captured,
        "released" => LedgerHoldStatus.Released,
        "expired" => LedgerHoldStatus.Expired,
        _ => throw new LedgerPostingException("ledger_hold_status_invalid"),
    };
}

public sealed record LedgerHoldChangedV1(
    string HoldId,
    string LedgerAccountId,
    string SourceService,
    string SourceReference,
    decimal OriginalAmount,
    decimal RemainingAmount,
    string Currency,
    string Status,
    long Version,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ChangedAt);
