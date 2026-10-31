using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Domain.Authentication;
using CoreBanking.IdentityAccess.Domain.Identities;
using CoreBanking.IdentityAccess.Infrastructure.Foundation;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IdentityAccess.Infrastructure.Identity;

/// <summary>Coordinates fail-closed local session state with durable provider-revocation work.</summary>
public sealed class SessionSecurityService
{
    private const string Producer = "identity-access";
    private const string EventType = "session_revoked";
    private readonly IdentityAccessDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly TimeProvider _timeProvider;

    public SessionSecurityService(IdentityAccessDbContext context, IdempotencyService idempotency, TimeProvider timeProvider)
    {
        _context = context;
        _idempotency = idempotency;
        _timeProvider = timeProvider;
    }

    public async Task<SessionRevocationResultV1> RevokeOwnSessionAsync(
        InstitutionId institutionId,
        ActorId actorId,
        string sessionId,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        IdempotencyKey key = IdempotencyKey.Create(institutionId.Value, "identity.session.revoke", actorId.Value, idempotencyKey);
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            key,
            IdempotencyKey.Fingerprint(sessionId),
            token => RevokeOnceAsync(institutionId, actorId, sessionId, correlationId, token),
            cancellationToken).ConfigureAwait(false);
        return outcome.Kind switch
        {
            IdempotencyOutcomeKind.Executed => Decode(outcome.Result, false),
            IdempotencyOutcomeKind.Replayed => Decode(outcome.Result, true),
            IdempotencyOutcomeKind.Conflict => throw new IdempotencyConflictException("The idempotency key was used for another session."),
            _ => throw new IdempotencyInProgressException("The session revocation is in progress."),
        };
    }

    /// <summary>
    /// Trusted provider callback boundary: creates local state only after the provider has completed
    /// authentication. The opaque provider reference is fingerprinted immediately and never stored.
    /// </summary>
    public async Task<SessionDetailsV1> RegisterProviderSessionAsync(
        InstitutionId institutionId,
        string identityId,
        string providerSessionReference,
        string? deviceId,
        AuthenticationStrength strength,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (expiresAt <= now || string.IsNullOrWhiteSpace(providerSessionReference))
        {
            throw new SessionSecurityException("session_invalid", "The provider session confirmation is invalid.");
        }
        bool activeIdentity = await _context.Identities.AnyAsync(record => record.IdentityId == identityId && record.Status == IdentityState.Active, cancellationToken).ConfigureAwait(false)
            && await _context.InstitutionMemberships.AnyAsync(record => record.IdentityId == identityId && record.Status == IdentityState.Active, cancellationToken).ConfigureAwait(false);
        if (!activeIdentity)
        {
            throw new SessionSecurityException("identity_not_active", "The identity is not active.");
        }
        string sessionId = Guid.NewGuid().ToString("N");
        _context.Sessions.Add(new SessionRecord
        {
            InstitutionId = institutionId.Value,
            SessionId = sessionId,
            IdentityId = identityId,
            DeviceId = deviceId,
            ProviderSessionFingerprint = IdentityLifecycleService.FingerprintProviderSubject("keycloak", "session", providerSessionReference),
            AuthenticationStrength = strength,
            IssuedAt = now,
            ExpiresAt = expiresAt,
            LastActivityAt = now,
            Status = SessionLifecycleState.Active,
            ConcurrencyVersion = 1,
        });
        _context.AuthenticationEvents.Add(new AuthenticationEventRecord
        {
            InstitutionId = institutionId.Value,
            AuthenticationEventId = Guid.NewGuid().ToString("N"),
            IdentityId = identityId,
            EventType = "session_issued",
            Outcome = AuthenticationOutcome.Succeeded,
            Detail = strength.ToString(),
            OccurredAt = now,
        });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new SessionDetailsV1(sessionId, "active", deviceId, strength.ToString().ToLowerInvariant(), now, expiresAt, now);
    }

    /// <summary>Records a provider-confirmed refresh after the provider owns the refresh-token exchange.</summary>
    public async Task<SessionDetailsV1> ConfirmProviderRefreshAsync(
        InstitutionId institutionId,
        ActorId actorId,
        string sessionId,
        string providerSessionReference,
        DateTimeOffset expiresAt,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        SessionRecord? session = await _context.Sessions.SingleOrDefaultAsync(record => record.SessionId == sessionId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        // A session whose own expiry has elapsed cannot be renewed by a refresh: the provider would
        // already refuse the exchange, and renewing locally would resurrect a session the platform
        // has already stopped honouring.
        if (session is null
            || session.IdentityId != actorId.Value
            || session.ConcurrencyVersion != expectedVersion
            || session.ExpiresAt <= now
            || expiresAt <= now
            || !SessionLifecycle.CanRefresh(session.Status))
        {
            throw new SessionSecurityException("session_refresh_refused", "The session refresh was refused.");
        }
        string fingerprint = IdentityLifecycleService.FingerprintProviderSubject("keycloak", "session", providerSessionReference);
        if (!string.Equals(fingerprint, session.ProviderSessionFingerprint, StringComparison.Ordinal))
        {
            session.Status = SessionLifecycleState.Compromised;
            session.RevokedAt = now;
            session.ProviderRevocationRequestedAt = now;
            session.ConcurrencyVersion++;
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            throw new SessionSecurityException("session_refresh_reuse_detected", "The session refresh was refused.");
        }
        session.ExpiresAt = expiresAt;
        session.LastActivityAt = now;
        session.Status = SessionLifecycleState.Active;
        session.ConcurrencyVersion++;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new SessionDetailsV1(session.SessionId, "active", session.DeviceId, session.AuthenticationStrength.ToString().ToLowerInvariant(), session.IssuedAt, session.ExpiresAt, session.LastActivityAt);
    }

    /// <summary>
    /// Lists the actor's sessions with the status that is true now. A session whose expiry has
    /// elapsed is reported as expired even before the maintenance sweep records the transition, so
    /// a caller never sees a lapsed session described as active.
    /// </summary>
    public async Task<IReadOnlyList<SessionDetailsV1>> ListOwnSessionsAsync(ActorId actorId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        List<SessionRecord> records = await _context.Sessions.AsNoTracking()
            .Where(record => record.IdentityId == actorId.Value)
            .OrderByDescending(record => record.LastActivityAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return records
            .Select(record => new SessionDetailsV1(
                record.SessionId,
                EffectiveStatus(record, now),
                record.DeviceId,
                record.AuthenticationStrength.ToString().ToLowerInvariant(),
                record.IssuedAt,
                record.ExpiresAt,
                record.LastActivityAt))
            .ToList();
    }

    /// <summary>
    /// Records a lock for the identity and scope and returns its identifier. At most one lock per
    /// identity and scope is unreleased, so a second attempt while a lock is still active keeps the
    /// existing lock and records the attempt as evidence instead of adding a duplicate. A lapsed
    /// lock that nobody released is closed at its expiry before the new lock is acquired.
    /// </summary>
    public async Task<string> RecordLockAsync(InstitutionId institutionId, string identityId, string scope, string reasonCode, DateTimeOffset? expiresAt, string correlationId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        List<SecurityLockRecord> unreleased = await _context.SecurityLocks
            .Where(record => record.IdentityId == identityId && record.Scope == scope && record.ReleasedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        SecurityLockRecord? active = unreleased.FirstOrDefault(record => record.ExpiresAt is null || record.ExpiresAt > now);
        if (active is not null)
        {
            AddAuthenticationEvent(institutionId.Value, identityId, "authentication_locked", AuthenticationOutcome.Failed, reasonCode, now);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return active.SecurityLockId;
        }

        foreach (SecurityLockRecord lapsed in unreleased)
        {
            lapsed.ReleasedAt = lapsed.ExpiresAt;
        }

        SecurityLockRecord securityLock = new()
        {
            InstitutionId = institutionId.Value,
            SecurityLockId = Guid.NewGuid().ToString("N"),
            IdentityId = identityId,
            Scope = scope,
            ReasonCode = reasonCode,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };
        _context.SecurityLocks.Add(securityLock);
        AddAuthenticationEvent(institutionId.Value, identityId, "authentication_locked", AuthenticationOutcome.Failed, reasonCode, now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return securityLock.SecurityLockId;
    }

    /// <summary>
    /// Reports whether an unreleased, unexpired lock exists for the identity and scope. Expiry is
    /// evaluated against the same clock that records locks, so a lock that has run out no longer
    /// blocks a new attempt even when nobody has released it.
    /// </summary>
    public async Task<bool> HasActiveLockAsync(string identityId, string scope, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        return await _context.SecurityLocks.AsNoTracking()
            .AnyAsync(
                record => record.IdentityId == identityId
                    && record.Scope == scope
                    && record.ReleasedAt == null
                    && (record.ExpiresAt == null || record.ExpiresAt > now),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Releases one lock. A repeated release is a no-operation that reports <c>false</c>, so an
    /// operator double-click cannot make the evidence disagree with itself.
    /// </summary>
    public async Task<bool> ReleaseLockAsync(string securityLockId, CancellationToken cancellationToken)
    {
        SecurityLockRecord? securityLock = await _context.SecurityLocks
            .SingleOrDefaultAsync(record => record.SecurityLockId == securityLockId, cancellationToken)
            .ConfigureAwait(false);
        if (securityLock is null)
        {
            throw new SessionSecurityException("security_lock_not_found", "The security lock was not found.");
        }

        if (securityLock.ReleasedAt is not null)
        {
            return false;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        securityLock.ReleasedAt = now;
        AddAuthenticationEvent(securityLock.InstitutionId, securityLock.IdentityId, "authentication_lock_released", AuthenticationOutcome.Succeeded, securityLock.ReasonCode, now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Marks sessions whose expiry has elapsed as <c>Expired</c> and records one authentication
    /// event per transition. This is a system maintenance pass: it deliberately reads across the
    /// tenant boundary with a comment in ADR-0004 terms, and no request path may call it.
    /// Returns the number of sessions transitioned so the caller can drain the backlog in batches.
    /// </summary>
    public async Task<int> ExpireLapsedSessionsAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "The batch size must be positive.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        List<SessionRecord> lapsed = await _context.Sessions
            .IgnoreQueryFilters()
            .Where(record =>
                (record.Status == SessionLifecycleState.Active || record.Status == SessionLifecycleState.Refreshed)
                && record.ExpiresAt <= now)
            .OrderBy(record => record.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (SessionRecord session in lapsed)
        {
            session.Status = SessionLifecycleState.Expired;
            session.ConcurrencyVersion++;
            AddAuthenticationEvent(session.InstitutionId, session.IdentityId, "session_expired", AuthenticationOutcome.Succeeded, "expiry_elapsed", now);
        }

        if (lapsed.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return lapsed.Count;
    }

    /// <summary>
    /// Closes lapsed locks that nobody released, setting the release instant to the expiry instant so
    /// the record states when the lock stopped applying. Also a system maintenance pass across the
    /// tenant boundary; it keeps the one-unreleased-lock-per-scope rule satisfiable.
    /// </summary>
    public async Task<int> ReleaseExpiredLocksAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "The batch size must be positive.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        List<SecurityLockRecord> lapsed = await _context.SecurityLocks
            .IgnoreQueryFilters()
            .Where(record => record.ReleasedAt == null && record.ExpiresAt != null && record.ExpiresAt <= now)
            .OrderBy(record => record.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (SecurityLockRecord securityLock in lapsed)
        {
            securityLock.ReleasedAt = securityLock.ExpiresAt;
            AddAuthenticationEvent(securityLock.InstitutionId, securityLock.IdentityId, "authentication_lock_expired", AuthenticationOutcome.Succeeded, securityLock.ReasonCode, now);
        }

        if (lapsed.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return lapsed.Count;
    }

    private static string EffectiveStatus(SessionRecord session, DateTimeOffset now) =>
        (session.Status is SessionLifecycleState.Active or SessionLifecycleState.Refreshed) && session.ExpiresAt <= now
            ? "expired"
            : session.Status.ToString().ToLowerInvariant();

    private void AddAuthenticationEvent(string institutionId, string identityId, string eventType, AuthenticationOutcome outcome, string detail, DateTimeOffset occurredAt) =>
        _context.AuthenticationEvents.Add(new AuthenticationEventRecord
        {
            InstitutionId = institutionId,
            AuthenticationEventId = Guid.NewGuid().ToString("N"),
            IdentityId = identityId,
            EventType = eventType,
            Outcome = outcome,
            Detail = detail,
            OccurredAt = occurredAt,
        });

    private async Task<string?> RevokeOnceAsync(InstitutionId institutionId, ActorId actorId, string sessionId, string correlationId, CancellationToken cancellationToken)
    {
        SessionRecord? session = await _context.Sessions.SingleOrDefaultAsync(record => record.SessionId == sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null || !string.Equals(session.IdentityId, actorId.Value, StringComparison.Ordinal))
        {
            throw new SessionSecurityException("session_not_found", "The session was not found.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string eventId = Guid.NewGuid().ToString("N");
        if (session.Status == SessionLifecycleState.Revoked)
        {
            return EventContract.Serialize(new SessionRevocationResultV1(sessionId, "revoked", session.RevocationEventId ?? eventId, false));
        }
        else
        {
            if (!SessionLifecycle.CanRevoke(session.Status))
            {
                throw new SessionSecurityException("session_not_revocable", "The session cannot be revoked in its current state.");
            }
            session.Status = SessionLifecycleState.Revoked;
            session.RevokedAt = now;
            session.ProviderRevocationRequestedAt = now;
            session.RevocationEventId = eventId;
            session.ConcurrencyVersion++;
        }

        _context.AuthenticationEvents.Add(new AuthenticationEventRecord
        {
            InstitutionId = institutionId.Value,
            AuthenticationEventId = Guid.NewGuid().ToString("N"),
            IdentityId = actorId.Value,
            EventType = "session_revoked",
            Outcome = AuthenticationOutcome.Succeeded,
            Detail = "self_service",
            OccurredAt = now,
        });
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = institutionId.Value,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = EventType,
            SchemaVersion = 1,
            Topic = EventContract.TopicFor(Producer, "identity", 1),
            BusinessReference = sessionId,
            CorrelationId = correlationId,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            Producer = Producer,
            Envelope = EventContract.Serialize(new { sessionId, status = "revoked" }),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return EventContract.Serialize(new SessionRevocationResultV1(sessionId, "revoked", eventId, false));
    }

    private static SessionRevocationResultV1 Decode(string? payload, bool replayed) =>
        (EventContract.Deserialize<SessionRevocationResultV1>(payload ?? string.Empty)
            ?? throw new InvalidOperationException("Invalid session idempotency result.")) with
        { Replayed = replayed };
}

public sealed class SessionSecurityException : Exception
{
    public SessionSecurityException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}
