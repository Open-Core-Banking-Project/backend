using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Owns the lien lifecycle: an immutable claim with a separate outstanding amount, partial release and
/// satisfaction as append-only entries, and dual control for statutory, tax and institution releases. The
/// reported availability always comes from the ledger. Decision: P1-E04-D11.
/// </summary>
public sealed class AccountLienService : IAccountLienService
{
    private const string Producer = "core-ledger";
    private const string ControlEventFamily = "account";
    private const string LienChangedEventType = "account_lien_changed";
    private const int ControlSchemaVersion = 1;
    private const string ActiveStatus = "active";
    private const string ClosedAccountStatus = "Closed";

    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILedgerAvailabilitySource _availability;

    public AccountLienService(
        CoreLedgerDbContext context,
        IdempotencyService idempotency,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        ILedgerAvailabilitySource availability)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
    }

    public async Task<AccountLienResult> CreateAsync(
        CreateAccountLienCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.AccountId, command.CorrelationId);
        LienPriorityCode code = AccountLien.ParseCode(command.PriorityCode);
        if (string.IsNullOrWhiteSpace(command.ReasonCode))
        {
            throw Reject("account_lien_reason_required", "A lien requires a reason code.");
        }

        if (code is LienPriorityCode.StatutoryFirst or LienPriorityCode.Tax
            && string.IsNullOrWhiteSpace(command.AuthorityReference))
        {
            throw Reject(AccountLienPolicy.AuthorityRequiredCode, "A statutory or tax lien requires an authority reference.");
        }

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            command.RequestId, command.AccountId, command.Amount.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
            command.CurrencyCode, command.PriorityCode, command.PriorityRank, command.ReasonCode));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.lien.create", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => CreateCoreAsync(command, code, token),
            cancellationToken).ConfigureAwait(false);
        return Decode(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountLienResult> ReleaseAsync(
        ReleaseAccountLienCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.AccountId, command.CorrelationId);
        if (command.Amount <= 0m)
        {
            throw Reject(AccountLienPolicy.AmountInvalidCode, "A release must be a positive amount.");
        }

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            command.AccountId, command.LienId, command.Amount.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), command.ReasonCode,
            command.ApprovalReference ?? string.Empty));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.lien.release", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => ReleaseCoreAsync(command, token),
            cancellationToken).ConfigureAwait(false);
        return Decode(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountLienResult> SatisfyAsync(
        SatisfyAccountLienCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.AccountId, command.CorrelationId);
        if (command.Amount <= 0m)
        {
            throw Reject(AccountLienPolicy.AmountInvalidCode, "A satisfaction must be a positive amount.");
        }

        if (string.IsNullOrWhiteSpace(command.SettlementReference))
        {
            throw Reject("account_lien_settlement_reference_required", "A satisfaction requires the ledger settlement reference.");
        }

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            command.AccountId, command.LienId, command.Amount.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), command.SettlementReference));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.lien.satisfy", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => SatisfyCoreAsync(command, token),
            cancellationToken).ConfigureAwait(false);
        return Decode(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<IReadOnlyList<AccountLienResult>> ListActiveAsync(
        InstitutionId institutionId,
        string accountId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (_tenantContext.CurrentInstitution != institutionId)
        {
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the account target.");
        }

        AccountRecord account = await LoadAccountAsync(institutionId, accountId, cancellationToken).ConfigureAwait(false);
        LedgerAvailabilitySnapshot snapshot = await _availability
            .GetAsync(institutionId, account.AccountId, account.CurrencyCode, cancellationToken)
            .ConfigureAwait(false);
        AccountLienRecord[] records = await _context.AccountLiens
            .AsNoTracking()
            .Where(record => record.AccountId == account.AccountId && record.Status == ActiveStatus)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<AccountLien> ordered = AccountLienPolicy.Order(records.Select(ToDomain));
        return ordered
            .Select(lien => ToResult(
                records.Single(record => record.LienId == lien.LienId),
                AccountLienPolicy.Combine(snapshot, ordered),
                asOf,
                replayed: false))
            .ToArray();
    }

    private async Task<string?> CreateCoreAsync(
        CreateAccountLienCommand command,
        LienPriorityCode code,
        CancellationToken cancellationToken)
    {
        AccountRecord account = await LoadAccountAsync(command.InstitutionId, command.AccountId, cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(account.Status, ClosedAccountStatus, StringComparison.Ordinal))
        {
            throw Reject("account_closed", "A closed account cannot receive a lien.");
        }

        if (!string.Equals(account.CurrencyCode, command.CurrencyCode?.Trim().ToUpperInvariant(), StringComparison.Ordinal))
        {
            throw Reject(AccountLienPolicy.CurrencyMismatchCode, "A lien must use the account currency.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string lienId = Guid.NewGuid().ToString("N");
        AccountLien lien;
        try
        {
            lien = AccountLienPolicy.Create(
                lienId, code, command.PriorityRank, command.Amount, command.EffectiveFrom, command.EffectiveTo);
        }
        catch (ArgumentException exception)
        {
            throw Reject("account_lien_request_invalid", exception.Message, exception);
        }

        AccountLienRecord[] existing = await _context.AccountLiens
            .AsNoTracking()
            .Where(record => record.AccountId == account.AccountId && record.Status == ActiveStatus)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        LedgerAvailabilitySnapshot snapshot = await _availability
            .GetAsync(command.InstitutionId, account.AccountId, account.CurrencyCode, cancellationToken)
            .ConfigureAwait(false);
        LienAvailabilityPosition position = AccountLienPolicy.Combine(snapshot, [.. existing.Select(ToDomain), lien]);
        if (position.ExceedsAvailable && !command.UncoveredAcknowledgedByApproverId)
        {
            throw Reject(
                AccountLienPolicy.UncoveredCode,
                "The lien exceeds available funds and needs an explicit approver acknowledgement.");
        }

        AccountLienRecord record = new()
        {
            InstitutionId = command.InstitutionId.Value,
            LienId = lienId,
            AccountId = account.AccountId,
            PriorityCode = AccountLien.ToToken(code),
            PriorityRank = command.PriorityRank,
            Amount = lien.Amount,
            OutstandingAmount = lien.OutstandingAmount,
            CurrencyCode = account.CurrencyCode,
            ReasonCode = command.ReasonCode.Trim(),
            AuthorityReference = command.AuthorityReference?.Trim(),
            Status = ActiveStatus,
            RequestId = command.RequestId.Trim(),
            CreatedByActorId = command.ActorId.Value,
            EffectiveFrom = command.EffectiveFrom,
            EffectiveTo = command.EffectiveTo,
            CreatedAt = now,
        };
        _context.AccountLiens.Add(record);
        AddAudit(command.InstitutionId, command.ActorId, AccountPermissions.LienCreate.Value, "account.lien.create", lienId, command.ReasonCode, command.CorrelationId, now);
        AddOutbox(command.InstitutionId.Value, record, command.CorrelationId, now);
        await SaveAsync("account_lien_conflict", "The account liens changed while the lien was being created.", cancellationToken).ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Serialize(ToResult(record, position, now, replayed: false));
    }

    private async Task<string?> ReleaseCoreAsync(
        ReleaseAccountLienCommand command,
        CancellationToken cancellationToken)
    {
        (AccountLienRecord record, AccountLien lien) = await LoadLienAsync(
            command.InstitutionId, command.AccountId, command.LienId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (lien.RequiresSecondApprover)
        {
            if (string.IsNullOrWhiteSpace(command.ApprovalReference))
            {
                throw Reject("account_lien_release_requires_approval", "Releasing this lien requires an approval reference.");
            }

            if (string.Equals(record.CreatedByActorId, command.ActorId.Value, StringComparison.Ordinal))
            {
                throw Reject("account_lien_dual_control_required", "The lien maker cannot release their own lien.");
            }
        }

        bool full = command.Amount == lien.OutstandingAmount;
        AccountLien updated;
        try
        {
            updated = full ? AccountLienPolicy.FullRelease(lien) : AccountLienPolicy.PartialRelease(lien, command.Amount);
        }
        catch (InvalidOperationException exception)
        {
            throw Reject(AccountLienPolicy.OverReleaseCode, exception.Message, exception);
        }

        record.OutstandingAmount = updated.OutstandingAmount;
        record.Status = updated.Status == LienStatus.Active ? ActiveStatus : "released";
        _context.AccountLienMovements.Add(new AccountLienMovementRecord
        {
            InstitutionId = command.InstitutionId.Value,
            MovementId = Guid.NewGuid().ToString("N"),
            LienId = record.LienId,
            MovementType = full ? "full_release" : "partial_release",
            Amount = command.Amount,
            OutstandingAfter = updated.OutstandingAmount,
            ReasonCode = command.ReasonCode.Trim(),
            AuthorityReference = command.AuthorityReference?.Trim(),
            ActorId = command.ActorId.Value,
            OccurredAt = now,
        });
        AddAudit(command.InstitutionId, command.ActorId, AccountPermissions.LienRelease.Value, "account.lien.release", record.LienId, command.ReasonCode, command.CorrelationId, now);
        AddOutbox(command.InstitutionId.Value, record, command.CorrelationId, now);
        await SaveAsync("account_lien_conflict", "The account liens changed while the release was being recorded.", cancellationToken).ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Serialize(
            await BuildResultAsync(command.InstitutionId, record, now, cancellationToken).ConfigureAwait(false));
    }

    private async Task<string?> SatisfyCoreAsync(
        SatisfyAccountLienCommand command,
        CancellationToken cancellationToken)
    {
        (AccountLienRecord record, AccountLien lien) = await LoadLienAsync(
            command.InstitutionId, command.AccountId, command.LienId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        AccountLien updated;
        try
        {
            updated = AccountLienPolicy.Satisfy(lien, command.Amount);
        }
        catch (InvalidOperationException exception)
        {
            throw Reject(AccountLienPolicy.OverReleaseCode, exception.Message, exception);
        }

        record.OutstandingAmount = updated.OutstandingAmount;
        record.Status = updated.Status == LienStatus.Active ? ActiveStatus : "released";
        _context.AccountLienMovements.Add(new AccountLienMovementRecord
        {
            InstitutionId = command.InstitutionId.Value,
            MovementId = Guid.NewGuid().ToString("N"),
            LienId = record.LienId,
            MovementType = "satisfaction",
            Amount = command.Amount,
            OutstandingAfter = updated.OutstandingAmount,
            ReasonCode = command.SettlementReference.Trim(),
            AuthorityReference = null,
            ActorId = command.ActorId.Value,
            OccurredAt = now,
        });
        AddAudit(command.InstitutionId, command.ActorId, AccountPermissions.LienRelease.Value, "account.lien.satisfy", record.LienId, command.SettlementReference, command.CorrelationId, now);
        AddOutbox(command.InstitutionId.Value, record, command.CorrelationId, now);
        await SaveAsync("account_lien_conflict", "The account liens changed while the settlement was being recorded.", cancellationToken).ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Serialize(
            await BuildResultAsync(command.InstitutionId, record, now, cancellationToken).ConfigureAwait(false));
    }

    private async Task<AccountLienResult> BuildResultAsync(
        InstitutionId institutionId,
        AccountLienRecord record,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        AccountLienRecord[] active = await _context.AccountLiens
            .AsNoTracking()
            .Where(candidate => candidate.AccountId == record.AccountId && candidate.Status == ActiveStatus)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        LedgerAvailabilitySnapshot snapshot = await _availability
            .GetAsync(institutionId, record.AccountId, record.CurrencyCode, cancellationToken)
            .ConfigureAwait(false);
        return ToResult(record, AccountLienPolicy.Combine(snapshot, active.Select(ToDomain)), changedAt, replayed: false);
    }

    private async Task<AccountRecord> LoadAccountAsync(
        InstitutionId institutionId,
        string accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return await _context.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.AccountId == accountId.Trim(), cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_not_found", "The account is not available in this institution.");
    }

    private async Task<(AccountLienRecord Record, AccountLien Lien)> LoadLienAsync(
        InstitutionId institutionId,
        string accountId,
        string lienId,
        CancellationToken cancellationToken)
    {
        AccountLienRecord record = await _context.AccountLiens
            .SingleOrDefaultAsync(
                candidate => candidate.LienId == lienId.Trim() && candidate.AccountId == accountId.Trim(),
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_lien_not_found", "The lien is not available for this account.");
        if (!string.Equals(record.Status, ActiveStatus, StringComparison.Ordinal))
        {
            throw Reject("account_lien_not_active", "The lien is already released.");
        }

        return (record, ToDomain(record));
    }

    private static AccountLien ToDomain(AccountLienRecord record) =>
        new(
            record.LienId,
            AccountLien.ParseCode(record.PriorityCode),
            record.PriorityRank,
            record.Amount,
            record.OutstandingAmount,
            string.Equals(record.Status, ActiveStatus, StringComparison.Ordinal) ? LienStatus.Active : LienStatus.Released,
            record.EffectiveFrom,
            record.EffectiveTo);

    private static AccountLienResult ToResult(
        AccountLienRecord record,
        LienAvailabilityPosition position,
        DateTimeOffset changedAt,
        bool replayed) =>
        new(
            record.LienId,
            record.AccountId,
            record.PriorityCode,
            record.PriorityRank,
            record.Amount,
            record.OutstandingAmount,
            record.CurrencyCode,
            record.Status,
            position,
            changedAt,
            replayed);

    private void AddOutbox(
        string institutionId,
        AccountLienRecord record,
        string correlationId,
        DateTimeOffset now)
    {
        string eventId = Guid.NewGuid().ToString("N");
        AccountLienChangedV1 payload = new(
            institutionId,
            record.AccountId,
            record.LienId,
            new MonetaryAmountV1(record.Amount, record.CurrencyCode),
            record.Status,
            record.PriorityCode,
            record.OutstandingAmount == record.Amount ? 1 : 2,
            now);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = LienChangedEventType,
            SchemaVersion = ControlSchemaVersion,
            OccurredAt = now,
            InstitutionId = institutionId,
            CorrelationId = correlationId,
            Producer = Producer,
            BusinessReference = record.LienId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = institutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = LienChangedEventType,
            SchemaVersion = ControlSchemaVersion,
            Topic = EventContract.TopicFor(Producer, ControlEventFamily, ControlSchemaVersion),
            BusinessReference = record.LienId,
            CorrelationId = correlationId,
            TraceParent = System.Diagnostics.Activity.Current?.Id,
            TraceState = System.Diagnostics.Activity.Current?.TraceStateString,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });
    }

    private void AddAudit(
        InstitutionId institutionId,
        ActorId actorId,
        string permission,
        string action,
        string resourceId,
        string reason,
        string correlationId,
        DateTimeOffset now)
    {
        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = institutionId.Value,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = actorId.Value,
            ActorType = "human",
            Permission = permission,
            Action = action,
            ResourceType = "account.lien",
            ResourceId = resourceId,
            Outcome = "succeeded",
            Reason = reason,
            CorrelationId = correlationId,
            OccurredAt = now,
        });
    }

    private async Task SaveAsync(string code, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            throw new AccountApplicationRejectedException(code, message, exception);
        }
    }

    private void ValidateTarget(
        InstitutionId institutionId,
        string idempotencyKey,
        string accountId,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (_tenantContext.CurrentInstitution != institutionId)
        {
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the account target.");
        }
    }

    private static AccountLienResult Decode(IdempotencyOutcome outcome, bool replayed)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (string.IsNullOrWhiteSpace(outcome.Result))
        {
            throw new InvalidOperationException("The lien command did not produce an idempotent result.");
        }

        AccountLienResult? decoded = System.Text.Json.JsonSerializer.Deserialize<AccountLienResult>(outcome.Result);
        return decoded is null
            ? throw new InvalidOperationException("The stored lien evidence is not readable.")
            : decoded with { Replayed = replayed };
    }

    private static AccountApplicationRejectedException Reject(string code, string message) => new(code, message);

    private static AccountApplicationRejectedException Reject(string code, string message, Exception inner) =>
        new(code, message, inner);
}
