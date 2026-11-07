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
/// Authorises statement and receipt requests. It validates the period, format and purpose against the published
/// policy, records the request without any content, and refuses with <c>statement_source_unavailable</c> when the
/// ledger cannot answer rather than substituting a summary. Artifact references are opaque, bound to the requester
/// and the account, short-lived and audited on every access. Decision: P1-E04-D15.
/// </summary>
public sealed class AccountStatementService : IAccountStatementService
{
    public const string RefusedStatus = "refused";
    public const string AwaitingArtifactStatus = "awaiting_artifact";
    public const string ReadyStatus = "ready";
    public const string ExpiredStatus = "expired";

    private const string Producer = "core-ledger";
    private const string StatementEventFamily = "account";
    private const string StatementRequestedEventType = "statement_requested";
    private const int StatementSchemaVersion = 1;

    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly IStatementPolicySource _policies;
    private readonly IBusinessDateSource _businessDates;
    private readonly ILedgerStatementQuerySource _ledger;

    public AccountStatementService(
        CoreLedgerDbContext context,
        IdempotencyService idempotency,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        IStatementPolicySource policies,
        IBusinessDateSource businessDates,
        ILedgerStatementQuerySource ledger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _businessDates = businessDates ?? throw new ArgumentNullException(nameof(businessDates));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    public async Task<AccountStatementRequestResult> RequestAsync(
        CreateAccountStatementRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.AccountId, command.CorrelationId);
        if (string.IsNullOrWhiteSpace(command.RequestId))
        {
            throw Reject("statement_request_id_required", "A statement request requires a request identifier.");
        }

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            command.RequestId.Trim(),
            command.AccountId.Trim(),
            command.PeriodFrom.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            command.PeriodTo.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            command.FormatCode ?? string.Empty,
            command.PurposeCode ?? string.Empty));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.statement.request", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => RequestCoreAsync(command, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<AccountStatementRequestResult>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountStatementRequestResult> GetAsync(
        InstitutionId institutionId,
        string accountId,
        string requestId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        if (_tenantContext.CurrentInstitution != institutionId)
        {
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the account target.");
        }

        AccountStatementRequestRecord record = await LoadRequestAsync(institutionId, accountId, requestId, cancellationToken)
            .ConfigureAwait(false);
        return ToResult(record, ExpireIfDue(record, _timeProvider.GetUtcNow()), replayed: false);
    }

    public async Task<StatementArtifactAuthorisationResult> AuthoriseArtifactAsync(
        AuthoriseStatementArtifactCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.AccountId, command.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RequestId);

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            command.RequestId.Trim(), command.AccountId.Trim(), command.RequesterTypeCode ?? string.Empty));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.statement.artifact", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => AuthoriseArtifactCoreAsync(command, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<StatementArtifactAuthorisationResult>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    private async Task<string?> RequestCoreAsync(
        CreateAccountStatementRequestCommand command,
        CancellationToken cancellationToken)
    {
        AccountRecord account = await _context.Accounts
            .SingleOrDefaultAsync(record => record.AccountId == command.AccountId.Trim(), cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_not_found", "The account is not available in this institution.");
        DateTimeOffset now = _timeProvider.GetUtcNow();
        StatementPolicyLookup lookup = await _policies.GetAsync(command.InstitutionId, now, cancellationToken)
            .ConfigureAwait(false);
        StatementPolicy policy = lookup.Policy
            ?? throw Reject(
                lookup.RejectionCode ?? AccountStatementPolicy.PolicyMissingCode,
                "The institution statement policy is unavailable.");
        DateOnly businessDate = await _businessDates
            .GetCurrentBusinessDateAsync(command.InstitutionId, cancellationToken)
            .ConfigureAwait(false);

        StatementRequestValidation validation = AccountStatementPolicy.Validate(
            policy,
            new AccountStatementFacts(
                DateOnly.FromDateTime(account.OpenedAt.UtcDateTime),
                businessDate,
                command.PeriodFrom,
                command.PeriodTo,
                command.FormatCode,
                command.PurposeCode));
        if (!validation.Accepted)
        {
            // Nothing is stored for an invalid request: it never becomes evidence of an authorised statement.
            throw Reject(validation.RefusalCodes[0], "The statement request does not satisfy the published policy.");
        }

        string requesterType = ValidateRequesterType(command.RequesterTypeCode);
        LedgerStatementQueryResult query = await _ledger
            .GetAsync(
                command.InstitutionId,
                account.AccountId,
                account.CurrencyCode,
                command.PeriodFrom,
                command.PeriodTo,
                cancellationToken)
            .ConfigureAwait(false);
        if (!query.Available)
        {
            AccountStatementRequestRecord refused = Build(command, account, policy, lookup.VersionId ?? string.Empty, requesterType, query, now);
            refused.Status = RefusedStatus;
            refused.RefusalCodes = query.UnavailableCode ?? AccountStatementPolicy.SourceUnavailableCode;
            _context.AccountStatementRequests.Add(refused);
            AddAudit(command.InstitutionId, command.ActorId, "account.statement.request", refused.RequestId, [refused.RefusalCodes], command.CorrelationId, now);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return Serialise(ToResult(refused, refused, replayed: false));
        }

        AccountStatementRequestRecord record = Build(command, account, policy, lookup.VersionId ?? string.Empty, requesterType, query, now);
        record.Status = AwaitingArtifactStatus;
        _context.AccountStatementRequests.Add(record);
        AddRequestedFact(command, record, now);
        AddAudit(command.InstitutionId, command.ActorId, "account.statement.request", record.RequestId, [], command.CorrelationId, now);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return Serialise(ToResult(record, record, replayed: false));
    }

    private async Task<string?> AuthoriseArtifactCoreAsync(
        AuthoriseStatementArtifactCommand command,
        CancellationToken cancellationToken)
    {
        AccountStatementRequestRecord record = await LoadRequestAsync(
            command.InstitutionId, command.AccountId, command.RequestId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (!string.Equals(record.Status, ReadyStatus, StringComparison.Ordinal))
        {
            ExpireIfDue(record, now);
            return Serialise(new StatementArtifactAuthorisationResult(
                record.RequestId,
                record.AccountId,
                string.Empty,
                record.IntegrityReference,
                now,
                record.RetentionExpiresAt,
                record.Status,
                Replayed: false));
        }

        AccountStatementArtifactReferenceRecord? existing = await _context.AccountStatementArtifactReferences
            .SingleOrDefaultAsync(candidate => candidate.RequestId == record.RequestId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null && existing.ExpiresAt > now)
        {
            existing.AccessCount++;
            existing.LastAccessedAt = now;
            AddAudit(command.InstitutionId, command.ActorId, "account.statement.retrieve", record.RequestId, [], command.CorrelationId, now);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return Serialise(new StatementArtifactAuthorisationResult(
                record.RequestId,
                record.AccountId,
                existing.ArtifactReference,
                record.IntegrityReference,
                existing.ExpiresAt,
                record.RetentionExpiresAt,
                record.Status,
                Replayed: true));
        }

        StatementPolicyLookup lookup = await _policies.GetAsync(command.InstitutionId, now, cancellationToken)
            .ConfigureAwait(false);
        StatementPolicy policy = lookup.Policy
            ?? throw Reject(
                lookup.RejectionCode ?? AccountStatementPolicy.PolicyMissingCode,
                "The institution statement policy is unavailable.");
        DateTimeOffset expiresAt = AccountStatementPolicy.ReferenceExpiry(policy, now);
        string artifactReference = Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        if (existing is null)
        {
            _context.AccountStatementArtifactReferences.Add(new AccountStatementArtifactReferenceRecord
            {
                InstitutionId = command.InstitutionId.Value,
                ReferenceId = Guid.NewGuid().ToString("N"),
                RequestId = record.RequestId,
                AccountId = record.AccountId,
                ArtifactReference = artifactReference,
                RequesterTypeCode = command.RequesterTypeCode?.Trim() ?? record.RequesterTypeCode,
                RequesterActorId = command.ActorId.Value,
                IssuedAt = now,
                ExpiresAt = expiresAt,
                AccessCount = 0,
                LastAccessedAt = null,
            });
        }
        else
        {
            existing.ArtifactReference = artifactReference;
            existing.RequesterTypeCode = command.RequesterTypeCode?.Trim() ?? record.RequesterTypeCode;
            existing.RequesterActorId = command.ActorId.Value;
            existing.IssuedAt = now;
            existing.ExpiresAt = expiresAt;
            existing.AccessCount++;
            existing.LastAccessedAt = now;
        }

        AddAudit(command.InstitutionId, command.ActorId, "account.statement.retrieve", record.RequestId, [], command.CorrelationId, now);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return Serialise(new StatementArtifactAuthorisationResult(
            record.RequestId,
            record.AccountId,
            artifactReference,
            record.IntegrityReference,
            expiresAt,
            record.RetentionExpiresAt,
            record.Status,
            Replayed: false));
    }

    private static AccountStatementRequestRecord Build(
        CreateAccountStatementRequestCommand command,
        AccountRecord account,
        StatementPolicy policy,
        string policyVersionId,
        string requesterType,
        LedgerStatementQueryResult query,
        DateTimeOffset now) =>
        new()
        {
            InstitutionId = command.InstitutionId.Value,
            RequestId = command.RequestId.Trim(),
            AccountId = account.AccountId,
            PeriodFrom = command.PeriodFrom,
            PeriodTo = command.PeriodTo,
            FormatCode = command.FormatCode.Trim().ToLowerInvariant(),
            PurposeCode = command.PurposeCode.Trim().ToLowerInvariant(),
            Status = RefusedStatus,
            PolicyVersionId = policyVersionId,
            RequesterTypeCode = requesterType,
            RequesterActorId = command.ActorId.Value,
            LedgerQueryReference = query.QueryReference,
            IntegrityReference = query.IntegrityReference,
            RefusalCodes = null,
            ArtifactReference = null,
            ArtifactReadyAt = null,
            RetentionExpiresAt = null,
            LegalHold = false,
            RequestedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

    /// <summary>
    /// A request whose artifact expired still answers, with an <c>expired</c> status: the record and its audit trail
    /// are never deleted. Decision: P1-E04-D15.
    /// </summary>
    private static AccountStatementRequestRecord ExpireIfDue(AccountStatementRequestRecord record, DateTimeOffset now)
    {
        if (string.Equals(record.Status, ReadyStatus, StringComparison.Ordinal)
            && record.RetentionExpiresAt is not null
            && record.RetentionExpiresAt <= now
            && !record.LegalHold)
        {
            record.Status = ExpiredStatus;
        }

        return record;
    }

    private async Task<AccountStatementRequestRecord> LoadRequestAsync(
        InstitutionId institutionId,
        string accountId,
        string requestId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        return await _context.AccountStatementRequests
            .SingleOrDefaultAsync(
                record => record.RequestId == requestId.Trim() && record.AccountId == accountId.Trim(),
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("statement_request_not_found", "The statement request is not available for this account.");
    }

    private static string ValidateRequesterType(string requesterTypeCode)
    {
        string code = requesterTypeCode?.Trim().ToLowerInvariant() ?? string.Empty;
        return code is "customer" or "staff"
            ? code
            : throw Reject(
                "statement_requester_type_invalid",
                "A statement requester must be the customer owner or an authorised staff role.");
    }

    private void AddRequestedFact(
        CreateAccountStatementRequestCommand command,
        AccountStatementRequestRecord record,
        DateTimeOffset now)
    {
        string eventId = Guid.NewGuid().ToString("N");
        StatementRequestedV1 payload = new(
            record.InstitutionId,
            record.RequestId,
            record.AccountId,
            record.PeriodFrom,
            record.PeriodTo,
            record.FormatCode,
            record.RequesterTypeCode,
            record.LedgerQueryReference,
            command.CorrelationId,
            now);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = StatementRequestedEventType,
            SchemaVersion = StatementSchemaVersion,
            OccurredAt = now,
            InstitutionId = record.InstitutionId,
            CorrelationId = command.CorrelationId,
            Producer = Producer,
            BusinessReference = record.RequestId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = record.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = StatementRequestedEventType,
            SchemaVersion = StatementSchemaVersion,
            Topic = EventContract.TopicFor(Producer, StatementEventFamily, StatementSchemaVersion),
            BusinessReference = record.RequestId,
            CorrelationId = command.CorrelationId,
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
        string requestId,
        IEnumerable<string> codes,
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
            Action = permission,
            ResourceType = "account.statement_request",
            ResourceId = requestId,
            Outcome = "succeeded",
            Reason = string.Join(',', codes),
            CorrelationId = correlationId,
            OccurredAt = now,
        });
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            throw new AccountApplicationRejectedException(
                "statement_request_conflict",
                "The statement request changed while it was being recorded.",
                exception);
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

    private static AccountStatementRequestResult ToResult(
        AccountStatementRequestRecord record,
        AccountStatementRequestRecord effective,
        bool replayed) =>
        new(
            record.InstitutionId,
            record.RequestId,
            record.AccountId,
            record.PeriodFrom,
            record.PeriodTo,
            record.FormatCode,
            record.PurposeCode,
            effective.Status,
            record.LedgerQueryReference,
            record.IntegrityReference,
            record.ArtifactReference,
            record.ArtifactReadyAt,
            record.RefusalCodes is null || record.RefusalCodes.Length == 0 ? [] : record.RefusalCodes.Split(','),
            record.RequesterTypeCode,
            record.RequestedAt,
            replayed);

    private static string Serialise<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static T Decode<T>(IdempotencyOutcome outcome, bool replayed)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (string.IsNullOrWhiteSpace(outcome.Result))
        {
            throw new InvalidOperationException("The statement command did not produce an idempotent result.");
        }

        T? decoded = System.Text.Json.JsonSerializer.Deserialize<T>(outcome.Result);
        if (decoded is null)
        {
            throw new InvalidOperationException("The stored statement evidence is not readable.");
        }

        return decoded switch
        {
            AccountStatementRequestResult request => (T)(object)(request with { Replayed = replayed }),
            StatementArtifactAuthorisationResult artifact => (T)(object)(artifact with { Replayed = replayed }),
            _ => decoded,
        };
    }

    private static AccountApplicationRejectedException Reject(string code, string message) => new(code, message);
}
