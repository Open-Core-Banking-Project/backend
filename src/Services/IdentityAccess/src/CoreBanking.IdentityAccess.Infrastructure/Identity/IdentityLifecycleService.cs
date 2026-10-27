using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Domain.Identities;
using CoreBanking.IdentityAccess.Infrastructure.Foundation;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Access;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Platform;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IdentityAccess.Infrastructure.Identity;

public sealed class IdentityLifecycleService
{
    public const string Producer = "identity-access";
    public const string EventFamily = "identity";
    public const string RegisteredEventType = "identity_registered";
    public const string StatusChangedEventType = "identity_status_changed";
    public const string RegisterOperation = "identity.identity.create";
    public const string StatusOperation = "identity.identity.update";
    public const int SchemaVersion = 1;

    private readonly IdentityAccessDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly TimeProvider _timeProvider;

    public IdentityLifecycleService(
        IdentityAccessDbContext context,
        IdempotencyService idempotency,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<IdentityRegistrationResultV1> RegisterAsync(
        InstitutionId institutionId,
        ActorId initiatingActorId,
        string idempotencyKey,
        RegisterIdentityV1 request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(initiatingActorId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        IdentityKind identityType = IdentityLifecycle.ParseKind(request.IdentityType);
        string displayName = RequiredBounded(request.DisplayName, 200, "identity_display_name_invalid");
        NormalizedProviderLink? providerLink = Normalize(request.ProviderLink);
        if (identityType == IdentityKind.Service && providerLink is not null)
        {
            throw new IdentityLifecycleException(
                "identity_service_provider_link_not_permitted",
                "Service identities use the service-client credential lifecycle.");
        }

        IdempotencyKey key = IdempotencyKey.Create(
            institutionId.Value,
            RegisterOperation,
            initiatingActorId.Value,
            idempotencyKey);
        string fingerprint = IdempotencyKey.Fingerprint(EventContract.Serialize(request));

        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            key,
            fingerprint,
            token => RegisterOnceAsync(
                institutionId,
                initiatingActorId,
                identityType,
                displayName,
                providerLink,
                correlationId,
                token),
            cancellationToken).ConfigureAwait(false);

        return outcome.Kind switch
        {
            IdempotencyOutcomeKind.Executed => Decode(outcome.Result, replayed: false),
            IdempotencyOutcomeKind.Replayed => Decode(outcome.Result, replayed: true),
            IdempotencyOutcomeKind.Conflict => throw new IdempotencyConflictException(
                "the idempotency key was already used with a materially different identity registration"),
            _ => throw new IdempotencyInProgressException(
                "an equivalent identity registration is still in progress"),
        };
    }

    public async Task<IdentityDetailsV1?> FindAsync(
        InstitutionId institutionId,
        string identityId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityId);

        var result = await (
            from identity in _context.Identities.AsNoTracking()
            join membership in _context.InstitutionMemberships.AsNoTracking()
                on new { identity.InstitutionId, identity.IdentityId }
                equals new { membership.InstitutionId, membership.IdentityId }
            join version in _context.EntitlementVersions.AsNoTracking()
                on new { identity.InstitutionId, identity.IdentityId }
                equals new { version.InstitutionId, version.IdentityId }
            where identity.InstitutionId == institutionId.Value && identity.IdentityId == identityId
            select new
            {
                identity.IdentityId,
                membership.MembershipId,
                identity.IdentityType,
                identity.DisplayName,
                identity.Status,
                ProviderLinked = _context.IdentityLinks.Any(link =>
                    link.InstitutionId == identity.InstitutionId &&
                    link.IdentityId == identity.IdentityId &&
                    link.Status == RecordStatus.Active),
                version.Version,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? null
            : new IdentityDetailsV1(
                result.IdentityId,
                result.MembershipId,
                IdentityLifecycle.ToContractValue(result.IdentityType),
                result.DisplayName,
                result.Status.ToString().ToLowerInvariant(),
                result.ProviderLinked,
                result.Version);
    }

    public async Task<IdentityStatusChangeResultV1> ChangeStatusAsync(
        InstitutionId institutionId,
        ActorId initiatingActorId,
        string identityId,
        string idempotencyKey,
        ChangeIdentityStatusV1 request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(initiatingActorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        IdentityState requestedState = IdentityLifecycle.ParseState(request.Status);
        string reason = RequiredBounded(request.Reason, 500, "identity_status_reason_invalid");
        if (request.ExpectedVersion < 1)
        {
            throw new IdentityLifecycleException(
                "identity_concurrency_version_invalid",
                "A positive expected version is required.");
        }

        IdempotencyKey key = IdempotencyKey.Create(
            institutionId.Value,
            StatusOperation,
            initiatingActorId.Value,
            idempotencyKey);
        string fingerprint = IdempotencyKey.Fingerprint(EventContract.Serialize(new
        {
            IdentityId = identityId,
            Request = request,
        }));

        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            key,
            fingerprint,
            token => ChangeStatusOnceAsync(
                institutionId,
                initiatingActorId,
                identityId,
                requestedState,
                reason,
                request.ExpectedVersion,
                correlationId,
                token),
            cancellationToken).ConfigureAwait(false);

        return outcome.Kind switch
        {
            IdempotencyOutcomeKind.Executed => DecodeStatus(outcome.Result, replayed: false),
            IdempotencyOutcomeKind.Replayed => DecodeStatus(outcome.Result, replayed: true),
            IdempotencyOutcomeKind.Conflict => throw new IdempotencyConflictException(
                "the idempotency key was already used with a materially different identity status request"),
            _ => throw new IdempotencyInProgressException(
                "an equivalent identity status request is still in progress"),
        };
    }

    public static string FingerprintProviderSubject(string provider, string realm, string subject)
    {
        string normalized = string.Concat(
            provider.Trim().ToLowerInvariant(), "\n",
            realm.Trim().ToLowerInvariant(), "\n",
            subject.Trim());
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(digest);
    }

    private async Task<string?> RegisterOnceAsync(
        InstitutionId institutionId,
        ActorId initiatingActorId,
        IdentityKind identityType,
        string displayName,
        NormalizedProviderLink? providerLink,
        string correlationId,
        CancellationToken cancellationToken)
    {
        InstitutionBoundaryRecord? boundary = await _context.InstitutionBoundaries
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (boundary is null || !string.Equals(boundary.Status, "active", StringComparison.Ordinal))
        {
            throw new IdentityLifecycleException(
                "identity_institution_inactive",
                "The institution is not active for identity registration.");
        }

        if (providerLink is not null)
        {
            bool duplicateLink = await _context.IdentityLinks.AnyAsync(
                link => link.Provider == providerLink.Provider
                    && link.Realm == providerLink.Realm
                    && link.SubjectFingerprint == providerLink.SubjectFingerprint,
                cancellationToken).ConfigureAwait(false);
            if (duplicateLink)
            {
                throw new IdentityLifecycleException(
                    "identity_provider_link_conflict",
                    "The provider identity is already linked.");
            }
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string identityId = Guid.NewGuid().ToString("N");
        string membershipId = Guid.NewGuid().ToString("N");
        string eventId = Guid.NewGuid().ToString("N");

        IdentityRecord identity = new()
        {
            InstitutionId = institutionId.Value,
            IdentityId = identityId,
            ActorType = identityType == IdentityKind.Service ? ActorType.Service : ActorType.Human,
            IdentityType = identityType,
            DisplayName = displayName,
            Status = IdentityState.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyVersion = 1,
        };
        _context.Identities.Add(identity);
        _context.InstitutionMemberships.Add(new InstitutionMembershipRecord
        {
            InstitutionId = institutionId.Value,
            MembershipId = membershipId,
            IdentityId = identityId,
            Status = IdentityState.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyVersion = 1,
        });
        _context.EntitlementVersions.Add(new EntitlementVersionRecord
        {
            InstitutionId = institutionId.Value,
            IdentityId = identityId,
            Version = 1,
            UpdatedAt = now,
        });

        if (providerLink is not null)
        {
            _context.IdentityLinks.Add(new ProviderIdentityLinkRecord
            {
                InstitutionId = institutionId.Value,
                IdentityLinkId = Guid.NewGuid().ToString("N"),
                IdentityId = identityId,
                Provider = providerLink.Provider,
                Realm = providerLink.Realm,
                SubjectFingerprint = providerLink.SubjectFingerprint,
                Status = RecordStatus.Active,
                CreatedAt = now,
            });
        }

        IdentityRegisteredV1 payload = new(
            identityId,
            membershipId,
            IdentityLifecycle.ToContractValue(identityType),
            "pending",
            providerLink is not null);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = RegisteredEventType,
            SchemaVersion = SchemaVersion,
            OccurredAt = now,
            InstitutionId = institutionId.Value,
            CorrelationId = correlationId,
            Producer = Producer,
            BusinessReference = identityId,
            Payload = EventContract.Serialize(payload),
        };

        _context.OperatorActionAuditEvents.Add(new OperatorActionAuditRecord
        {
            InstitutionId = institutionId.Value,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = initiatingActorId.Value,
            Action = RegisterOperation,
            ResourceType = "identity",
            ResourceId = identityId,
            Reason = "identity_registration",
            CorrelationId = correlationId,
            OccurredAt = now,
        });
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = institutionId.Value,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = RegisteredEventType,
            SchemaVersion = SchemaVersion,
            Topic = EventContract.TopicFor(Producer, EventFamily, SchemaVersion),
            BusinessReference = identityId,
            CorrelationId = correlationId,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Encode(new IdentityRegistrationResultV1(
            identityId,
            membershipId,
            payload.IdentityType,
            payload.Status,
            payload.ProviderLinked,
            eventId,
            false));
    }

    private async Task<string?> ChangeStatusOnceAsync(
        InstitutionId institutionId,
        ActorId initiatingActorId,
        string identityId,
        IdentityState requestedState,
        string reason,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken)
    {
        IdentityRecord? identity = await _context.Identities
            .SingleOrDefaultAsync(record => record.IdentityId == identityId, cancellationToken)
            .ConfigureAwait(false);
        InstitutionMembershipRecord? membership = await _context.InstitutionMemberships
            .SingleOrDefaultAsync(record => record.IdentityId == identityId, cancellationToken)
            .ConfigureAwait(false);
        EntitlementVersionRecord? entitlementVersion = await _context.EntitlementVersions
            .SingleOrDefaultAsync(record => record.IdentityId == identityId, cancellationToken)
            .ConfigureAwait(false);

        if (identity is null || membership is null || entitlementVersion is null)
        {
            throw new IdentityLifecycleException("identity_not_found", "The identity was not found.");
        }

        if (identity.ConcurrencyVersion != expectedVersion)
        {
            throw new IdentityLifecycleException(
                "identity_concurrency_conflict",
                "The identity changed after it was read.");
        }

        IdentityState previousState = identity.Status;
        if (!IdentityLifecycle.CanTransition(previousState, requestedState))
        {
            throw new IdentityLifecycleException(
                "identity_status_transition_invalid",
                "The requested identity status transition is not permitted.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        identity.Status = requestedState;
        identity.UpdatedAt = now;
        identity.ConcurrencyVersion++;
        membership.Status = requestedState;
        membership.UpdatedAt = now;
        membership.ConcurrencyVersion++;
        entitlementVersion.Version++;
        entitlementVersion.UpdatedAt = now;

        string eventId = Guid.NewGuid().ToString("N");
        string previousStatus = previousState.ToString().ToLowerInvariant();
        string status = requestedState.ToString().ToLowerInvariant();
        string reasonCode = "identity_status_changed";
        IdentityStatusChangedV1 payload = new(
            identityId,
            membership.MembershipId,
            previousStatus,
            status,
            entitlementVersion.Version,
            reasonCode);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = StatusChangedEventType,
            SchemaVersion = SchemaVersion,
            OccurredAt = now,
            InstitutionId = institutionId.Value,
            CorrelationId = correlationId,
            Producer = Producer,
            BusinessReference = identityId,
            Payload = EventContract.Serialize(payload),
        };

        _context.OperatorActionAuditEvents.Add(new OperatorActionAuditRecord
        {
            InstitutionId = institutionId.Value,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = initiatingActorId.Value,
            Action = StatusOperation,
            ResourceType = "identity",
            ResourceId = identityId,
            Reason = reason,
            CorrelationId = correlationId,
            OccurredAt = now,
        });
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = institutionId.Value,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = StatusChangedEventType,
            SchemaVersion = SchemaVersion,
            Topic = EventContract.TopicFor(Producer, EventFamily, SchemaVersion),
            BusinessReference = identityId,
            CorrelationId = correlationId,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return EventContract.Serialize(new IdentityStatusChangeResultV1(
            identityId,
            previousStatus,
            status,
            entitlementVersion.Version,
            eventId,
            false));
    }

    private static NormalizedProviderLink? Normalize(ProviderIdentityLinkV1? link)
    {
        if (link is null)
        {
            return null;
        }

        string provider = RequiredBounded(link.Provider, 100, "identity_provider_invalid").ToLowerInvariant();
        string realm = RequiredBounded(link.Realm, 200, "identity_provider_realm_invalid").ToLowerInvariant();
        string subject = RequiredBounded(link.Subject, 500, "identity_provider_subject_invalid");
        return new NormalizedProviderLink(provider, realm, FingerprintProviderSubject(provider, realm, subject));
    }

    private static string RequiredBounded(string value, int maximumLength, string code)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
        {
            throw new IdentityLifecycleException(code, "A required identity field is invalid.");
        }

        return value.Trim();
    }

    private static string Encode(IdentityRegistrationResultV1 result) => EventContract.Serialize(result with { Replayed = false });

    private static IdentityRegistrationResultV1 Decode(string? result, bool replayed)
    {
        IdentityRegistrationResultV1 restored = EventContract.Deserialize<IdentityRegistrationResultV1>(result ?? string.Empty)
            ?? throw new InvalidOperationException("The recorded idempotency outcome is not an identity registration result.");
        return restored with { Replayed = replayed };
    }

    private static IdentityStatusChangeResultV1 DecodeStatus(string? result, bool replayed)
    {
        IdentityStatusChangeResultV1 restored = EventContract.Deserialize<IdentityStatusChangeResultV1>(result ?? string.Empty)
            ?? throw new InvalidOperationException("The recorded idempotency outcome is not an identity status result.");
        return restored with { Replayed = replayed };
    }

    private sealed record NormalizedProviderLink(string Provider, string Realm, string SubjectFingerprint);
}

public sealed class IdentityLifecycleException : Exception
{
    public IdentityLifecycleException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
