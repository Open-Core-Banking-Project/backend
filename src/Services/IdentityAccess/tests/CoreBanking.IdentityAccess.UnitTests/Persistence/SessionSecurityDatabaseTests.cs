using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Domain.Authentication;
using CoreBanking.IdentityAccess.Domain.Identities;
using CoreBanking.IdentityAccess.Infrastructure.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Persistence;

/// <summary>
/// Session lifecycle and security lock behaviour against a real <c>identity_access_db</c>, as the
/// application principal. The suite needs the local infrastructure stack, is excluded from the
/// default run and is executed by <c>scripts/db-verify.ps1</c> after the migrations are applied.
/// Decision: ADR-0004, ADR-0008, ADR-0010.
/// </summary>
[Trait("Category", "Database")]
public sealed class SessionSecurityDatabaseTests : IDisposable
{
    private const string ConnectionStringVariableName = "ConnectionStrings__IdentityAccess";
    private const string LockScope = "authentication";

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("session-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _actor = ActorId.From("session-actor-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));

    public SessionSecurityDatabaseTests()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringVariableName} to the application principal connection string, or run scripts/db-verify.ps1.");
        }

        _connectionString = configured;
        SeedActiveIdentity(_institution, _actor.Value);
    }

    [Fact]
    public async Task A_confirmed_provider_session_is_stored_active_with_a_fingerprint_and_version()
    {
        SessionDetailsV1 details = await RegisterSessionAsync("provider-session-1");

        await using IdentityAccessDbContext context = CreateContext(_institution);
        SessionRecord session = await context.Sessions.SingleAsync(record => record.SessionId == details.SessionId);

        Assert.Equal(SessionLifecycleState.Active, session.Status);
        Assert.Equal(1L, session.ConcurrencyVersion);
        Assert.Equal(details.LastActivityAt, session.LastActivityAt);
        Assert.Equal(session.IssuedAt, session.LastActivityAt);
        Assert.Equal(64, session.ProviderSessionFingerprint.Length);
        Assert.DoesNotContain("provider-session-1", session.ProviderSessionFingerprint, StringComparison.Ordinal);
        Assert.Equal("active", details.Status);
        Assert.Single(context.AuthenticationEvents.Where(evidence => evidence.EventType == "session_issued"));
    }

    [Fact]
    public async Task A_matching_refresh_extends_the_session_and_increments_the_version()
    {
        SessionDetailsV1 details = await RegisterSessionAsync("provider-session-2");

        await using IdentityAccessDbContext context = CreateContext(_institution);
        SessionSecurityService service = CreateService(context, _time);
        _time.Advance(TimeSpan.FromMinutes(5));
        DateTimeOffset newExpiry = _time.GetUtcNow().AddHours(2);

        await service.ConfirmProviderRefreshAsync(_institution, _actor, details.SessionId, "provider-session-2", newExpiry, 1L, CancellationToken.None);

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        SessionRecord session = await verification.Sessions.SingleAsync(record => record.SessionId == details.SessionId);
        Assert.Equal(2L, session.ConcurrencyVersion);
        Assert.Equal(newExpiry, session.ExpiresAt);
        Assert.Equal(_time.GetUtcNow(), session.LastActivityAt);
        Assert.Equal(SessionLifecycleState.Active, session.Status);
    }

    [Fact]
    public async Task A_refresh_with_a_stale_version_is_refused_and_changes_nothing()
    {
        SessionDetailsV1 details = await RegisterSessionAsync("provider-session-3");

        await using IdentityAccessDbContext context = CreateContext(_institution);
        SessionSecurityService service = CreateService(context, _time);

        await Assert.ThrowsAsync<SessionSecurityException>(() => service.ConfirmProviderRefreshAsync(
            _institution, _actor, details.SessionId, "provider-session-3", _time.GetUtcNow().AddHours(1), 99L, CancellationToken.None));

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        Assert.Equal(1L, (await verification.Sessions.SingleAsync(record => record.SessionId == details.SessionId)).ConcurrencyVersion);
    }

    [Fact]
    public async Task A_refresh_with_a_different_provider_reference_is_treated_as_reuse_and_revokes_the_session()
    {
        SessionDetailsV1 details = await RegisterSessionAsync("provider-session-4");

        await using IdentityAccessDbContext context = CreateContext(_institution);
        SessionSecurityService service = CreateService(context, _time);

        await Assert.ThrowsAsync<SessionSecurityException>(() => service.ConfirmProviderRefreshAsync(
            _institution, _actor, details.SessionId, "provider-session-impostor", _time.GetUtcNow().AddHours(1), 1L, CancellationToken.None));

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        SessionRecord session = await verification.Sessions.SingleAsync(record => record.SessionId == details.SessionId);
        Assert.Equal(SessionLifecycleState.Compromised, session.Status);
        Assert.NotNull(session.RevokedAt);
        Assert.NotNull(session.ProviderRevocationRequestedAt);
    }

    [Fact]
    public async Task A_refresh_of_an_expired_session_is_refused()
    {
        SessionDetailsV1 details = await RegisterSessionAsync("provider-session-5");

        await using IdentityAccessDbContext context = CreateContext(_institution);
        SessionSecurityService service = CreateService(context, _time);
        _time.Advance(TimeSpan.FromHours(2));

        await Assert.ThrowsAsync<SessionSecurityException>(() => service.ConfirmProviderRefreshAsync(
            _institution, _actor, details.SessionId, "provider-session-5", _time.GetUtcNow().AddHours(1), 1L, CancellationToken.None));
    }

    [Fact]
    public async Task Listing_returns_only_the_actors_sessions_most_recent_first()
    {
        await RegisterSessionAsync("provider-session-6");
        await RegisterSessionAsync("provider-session-7");
        await RegisterSessionAsync("provider-session-8", actorId: ActorId.From("other-actor-" + Guid.NewGuid().ToString("N")[..8]));

        await using IdentityAccessDbContext context = CreateContext(_institution);
        IReadOnlyList<SessionDetailsV1> sessions = await CreateService(context, _time).ListOwnSessionsAsync(_actor, CancellationToken.None);

        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session => Assert.Equal("active", session.Status));
        Assert.True(sessions[0].LastActivityAt >= sessions[1].LastActivityAt);
    }

    [Fact]
    public async Task Revocation_records_the_event_reference_and_is_idempotent()
    {
        SessionDetailsV1 details = await RegisterSessionAsync("provider-session-9");

        await using IdentityAccessDbContext context = CreateContext(_institution);
        SessionRevocationResultV1 first = await CreateService(context, _time).RevokeOwnSessionAsync(
            _institution, _actor, details.SessionId, "idempotency-1", "correlation-1", CancellationToken.None);

        await using IdentityAccessDbContext second = CreateContext(_institution);
        SessionRevocationResultV1 repeated = await CreateService(second, _time).RevokeOwnSessionAsync(
            _institution, _actor, details.SessionId, "idempotency-1", "correlation-1", CancellationToken.None);

        Assert.False(first.Replayed);
        Assert.True(repeated.Replayed);
        Assert.Equal(first.EventId, repeated.EventId);
        Assert.Equal("revoked", first.Status);

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        SessionRecord session = await verification.Sessions.SingleAsync(record => record.SessionId == details.SessionId);
        Assert.Equal(SessionLifecycleState.Revoked, session.Status);
        Assert.Equal(first.EventId, session.RevocationEventId);
        Assert.NotNull(session.RevokedAt);
        Assert.NotNull(session.ProviderRevocationRequestedAt);
        Assert.Single(verification.OutboxMessages.Where(record => record.EventType == "session_revoked"));
    }

    [Fact]
    public async Task Revocation_writes_exactly_one_provider_revocation_request_for_repeated_attempts_with_distinct_keys()
    {
        SessionDetailsV1 details = await RegisterSessionAsync("provider-session-9");

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            await CreateService(context, _time).RevokeOwnSessionAsync(
                _institution, _actor, details.SessionId, "idempotency-a", "correlation-a", CancellationToken.None);
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            SessionRevocationResultV1 second = await CreateService(context, _time).RevokeOwnSessionAsync(
                _institution, _actor, details.SessionId, "idempotency-b", "correlation-b", CancellationToken.None);

            Assert.False(second.Replayed);
            Assert.Equal("revoked", second.Status);
        }

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        Assert.Single(verification.OutboxMessages.Where(record => record.EventType == "session_revoked"));
        Assert.Single(verification.Sessions.Where(record => record.SessionId == details.SessionId));
    }

    [Fact]
    public async Task Revocation_of_another_institutions_session_is_not_found()
    {
        SessionDetailsV1 details = await RegisterSessionAsync("provider-session-20");

        await using IdentityAccessDbContext context = CreateContext(_otherInstitution);
        SessionSecurityException exception = await Assert.ThrowsAsync<SessionSecurityException>(() =>
            CreateService(context, _time).RevokeOwnSessionAsync(
                _otherInstitution, _actor, details.SessionId, "idempotency-cross", "correlation-cross", CancellationToken.None));

        Assert.Equal("session_not_found", exception.Code);
    }

    [Fact]
    public async Task A_concurrent_write_to_the_same_session_fails_on_the_concurrency_version()
    {
        SessionDetailsV1 details = await RegisterSessionAsync("provider-session-21");

        await using IdentityAccessDbContext first = CreateContext(_institution);
        await using IdentityAccessDbContext second = CreateContext(_institution);

        SessionRecord firstCopy = await first.Sessions.SingleAsync(record => record.SessionId == details.SessionId);
        SessionRecord secondCopy = await second.Sessions.SingleAsync(record => record.SessionId == details.SessionId);

        firstCopy.ConcurrencyVersion = 2L;
        firstCopy.LastActivityAt = _time.GetUtcNow();
        await first.SaveChangesAsync(CancellationToken.None);

        secondCopy.ConcurrencyVersion = 2L;
        secondCopy.LastActivityAt = _time.GetUtcNow().AddSeconds(1);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_duplicate_provider_fingerprint_is_rejected_by_the_tenant_unique_index()
    {
        SessionDetailsV1 details = await RegisterSessionAsync("provider-session-22");

        await using IdentityAccessDbContext context = CreateContext(_institution);
        string fingerprint = (await context.Sessions.SingleAsync(record => record.SessionId == details.SessionId)).ProviderSessionFingerprint;

        context.Sessions.Add(NewSession("duplicate-" + Guid.NewGuid().ToString("N")[..8], fingerprint, SessionLifecycleState.Active));

        DbUpdateException exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(CancellationToken.None));
        Assert.Equal("23505", Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    [Fact]
    public async Task An_unknown_session_status_is_rejected_by_the_check_constraint()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO identity.sessions
                (institution_id, session_id, identity_id, authentication_strength, issued_at, expires_at,
                 concurrency_version, last_activity_at, provider_session_fingerprint, status)
            VALUES ({0}, {1}, {2}, 'SingleFactor', now(), now() + interval '1 hour', 1, now(), {3}, 'Bogus')
            """,
            _institution.Value, "invalid-status", _actor.Value, "synthetic-fingerprint"));

        Assert.Equal("23514", exception.SqlState);
        Assert.Equal("ck_sessions_status", exception.ConstraintName);
    }

    [Fact]
    public async Task A_lock_is_active_until_it_expires_or_is_released()
    {
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            await CreateService(context, _time).RecordLockAsync(
                _institution, _actor.Value, LockScope, "failed_attempts", _time.GetUtcNow().AddMinutes(15), "correlation-lock", CancellationToken.None);
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            Assert.True(await CreateService(context, _time).HasActiveLockAsync(_actor.Value, LockScope, CancellationToken.None));
        }

        _time.Advance(TimeSpan.FromMinutes(16));

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            Assert.False(await CreateService(context, _time).HasActiveLockAsync(_actor.Value, LockScope, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Releasing_a_lock_is_idempotent_and_allows_a_new_lock_to_be_acquired()
    {
        string lockId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            await CreateService(context, _time).RecordLockAsync(
                _institution, _actor.Value, LockScope, "failed_attempts", _time.GetUtcNow().AddHours(1), "correlation-lock-1", CancellationToken.None);
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            lockId = (await context.SecurityLocks.AsNoTracking().SingleAsync()).SecurityLockId;
            Assert.True(await CreateService(context, _time).ReleaseLockAsync(lockId, CancellationToken.None));
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            Assert.False(await CreateService(context, _time).ReleaseLockAsync(lockId, CancellationToken.None));
            Assert.False(await CreateService(context, _time).HasActiveLockAsync(_actor.Value, LockScope, CancellationToken.None));
        }

        _time.Advance(TimeSpan.FromMinutes(1));

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            await CreateService(context, _time).RecordLockAsync(
                _institution, _actor.Value, LockScope, "failed_attempts", _time.GetUtcNow().AddHours(1), "correlation-lock-2", CancellationToken.None);
        }

        await using (IdentityAccessDbContext verification = CreateContext(_institution))
        {
            Assert.True(await CreateService(verification, _time).HasActiveLockAsync(_actor.Value, LockScope, CancellationToken.None));
            Assert.Equal(2, verification.SecurityLocks.Count());
            Assert.Single(verification.AuthenticationEvents.Where(evidence => evidence.EventType == "authentication_lock_released"));
        }
    }

    [Fact]
    public async Task A_lock_is_invisible_to_another_institution()
    {
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            await CreateService(context, _time).RecordLockAsync(
                _institution, _actor.Value, LockScope, "failed_attempts", _time.GetUtcNow().AddHours(1), "correlation-lock", CancellationToken.None);
        }

        await using IdentityAccessDbContext other = CreateContext(_otherInstitution);
        SessionSecurityService service = CreateService(other, _time);

        Assert.False(await service.HasActiveLockAsync(_actor.Value, LockScope, CancellationToken.None));
        Assert.Empty(other.SecurityLocks.ToList());
    }

    [Fact]
    public async Task Recording_a_lock_while_one_is_active_keeps_a_single_lock_and_records_the_attempt()
    {
        string firstLockId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            firstLockId = await CreateService(context, _time).RecordLockAsync(
                _institution, _actor.Value, LockScope, "failed_attempts", _time.GetUtcNow().AddHours(1), "correlation-lock-1", CancellationToken.None);
        }

        _time.Advance(TimeSpan.FromMinutes(1));

        string secondLockId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            secondLockId = await CreateService(context, _time).RecordLockAsync(
                _institution, _actor.Value, LockScope, "failed_attempts", _time.GetUtcNow().AddHours(2), "correlation-lock-2", CancellationToken.None);
        }

        Assert.Equal(firstLockId, secondLockId);

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        Assert.Single(verification.SecurityLocks);
        Assert.Equal(2, verification.AuthenticationEvents.Count(evidence => evidence.EventType == "authentication_locked"));
    }

    [Fact]
    public async Task An_expired_unreleased_lock_is_closed_before_a_new_lock_is_acquired()
    {
        DateTimeOffset firstExpiry = _time.GetUtcNow().AddMinutes(15);
        string firstLockId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            firstLockId = await CreateService(context, _time).RecordLockAsync(
                _institution, _actor.Value, LockScope, "failed_attempts", firstExpiry, "correlation-lock-1", CancellationToken.None);
        }

        _time.Advance(TimeSpan.FromMinutes(16));

        string secondLockId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            secondLockId = await CreateService(context, _time).RecordLockAsync(
                _institution, _actor.Value, LockScope, "failed_attempts", _time.GetUtcNow().AddHours(1), "correlation-lock-2", CancellationToken.None);
        }

        Assert.NotEqual(firstLockId, secondLockId);

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        SecurityLockRecord closed = await verification.SecurityLocks.SingleAsync(record => record.SecurityLockId == firstLockId);
        SecurityLockRecord active = await verification.SecurityLocks.SingleAsync(record => record.SecurityLockId == secondLockId);
        Assert.Equal(firstExpiry, closed.ReleasedAt);
        Assert.Null(active.ReleasedAt);
    }

    [Fact]
    public async Task The_sweep_closes_lapsed_locks_at_their_expiry_instant()
    {
        DateTimeOffset expiry = _time.GetUtcNow().AddMinutes(15);
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            await CreateService(context, _time).RecordLockAsync(
                _institution, _actor.Value, LockScope, "failed_attempts", expiry, "correlation-lock", CancellationToken.None);
        }

        _time.Advance(TimeSpan.FromMinutes(16));

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            Assert.Equal(1, await CreateService(context, _time).ReleaseExpiredLocksAsync(100, CancellationToken.None));
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            Assert.Equal(0, await CreateService(context, _time).ReleaseExpiredLocksAsync(100, CancellationToken.None));
        }

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        SecurityLockRecord closed = await verification.SecurityLocks.SingleAsync();
        Assert.Equal(expiry, closed.ReleasedAt);
        Assert.Single(verification.AuthenticationEvents.Where(evidence => evidence.EventType == "authentication_lock_expired"));
    }

    [Fact]
    public async Task A_second_unreleased_lock_for_the_same_scope_is_rejected()
    {
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            await CreateService(context, _time).RecordLockAsync(
                _institution, _actor.Value, LockScope, "failed_attempts", _time.GetUtcNow().AddHours(1), "correlation-lock", CancellationToken.None);
        }

        await using IdentityAccessDbContext context2 = CreateContext(_institution);
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => context2.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO security.locks
                (institution_id, security_lock_id, identity_id, scope, reason_code, created_at, expires_at, released_at)
            VALUES ({0}, {1}, {2}, {3}, 'duplicate_attempt', now(), now() + interval '1 hour', NULL)
            """,
            _institution.Value, "duplicate-lock", _actor.Value, LockScope));

        Assert.Equal("23505", exception.SqlState);
        Assert.Equal("ux_locks_unreleased_scope", exception.ConstraintName);
    }

    [Fact]
    public async Task A_lapsed_session_is_marked_expired_once_by_the_sweep()
    {
        SessionDetailsV1 details = await RegisterSessionAsync("provider-session-sweep");
        _time.Advance(TimeSpan.FromHours(2));

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            Assert.Equal(1, await CreateService(context, _time).ExpireLapsedSessionsAsync(100, CancellationToken.None));
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            Assert.Equal(0, await CreateService(context, _time).ExpireLapsedSessionsAsync(100, CancellationToken.None));
        }

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        SessionRecord session = await verification.Sessions.SingleAsync(record => record.SessionId == details.SessionId);
        Assert.Equal(SessionLifecycleState.Expired, session.Status);
        Assert.Equal(2L, session.ConcurrencyVersion);
        Assert.Single(verification.AuthenticationEvents.Where(evidence => evidence.EventType == "session_expired"));

        IReadOnlyList<SessionDetailsV1> sessions = await CreateService(verification, _time).ListOwnSessionsAsync(_actor, CancellationToken.None);
        Assert.Equal("expired", sessions.Single().Status);
    }

    [Fact]
    public async Task Listing_reports_a_lapsed_session_as_expired_before_the_sweep_runs()
    {
        await RegisterSessionAsync("provider-session-lapsed");
        _time.Advance(TimeSpan.FromHours(2));

        await using IdentityAccessDbContext context = CreateContext(_institution);
        IReadOnlyList<SessionDetailsV1> sessions = await CreateService(context, _time).ListOwnSessionsAsync(_actor, CancellationToken.None);

        Assert.Equal("expired", sessions.Single().Status);
        Assert.Equal(SessionLifecycleState.Active, (await context.Sessions.SingleAsync()).Status);
    }

    [Fact]
    public async Task The_committed_migration_created_the_lifecycle_columns_and_the_lock_table()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);

        string[] sessionColumns = await ColumnNamesAsync(context, "identity", "sessions");
        Assert.Contains("status", sessionColumns);
        Assert.Contains("last_activity_at", sessionColumns);
        Assert.Contains("provider_session_fingerprint", sessionColumns);
        Assert.Contains("concurrency_version", sessionColumns);
        Assert.Contains("provider_revocation_requested_at", sessionColumns);
        Assert.Contains("revocation_event_id", sessionColumns);

        string[] lockColumns = await ColumnNamesAsync(context, "security", "locks");
        Assert.Equal(
            ["institution_id", "security_lock_id", "identity_id", "scope", "reason_code", "created_at", "expires_at", "released_at"],
            lockColumns);

        string[] sessionConstraints = await CheckConstraintNamesAsync("identity.sessions");
        Assert.Contains("ck_sessions_status", sessionConstraints);
        Assert.Contains("ck_sessions_concurrency_version", sessionConstraints);

        string[] lockConstraints = await CheckConstraintNamesAsync("security.locks");
        Assert.Contains("ck_locks_expiry_after_creation", lockConstraints);
        Assert.Contains("ck_locks_release_after_creation", lockConstraints);

        string[] lockIndexes = await IndexNamesAsync(context, "security", "locks");
        Assert.Contains("ux_locks_unreleased_scope", lockIndexes);
        Assert.DoesNotContain("IX_locks_institution_id_identity_id_scope", lockIndexes);
    }

    [Fact]
    public async Task The_application_principal_has_data_privileges_and_no_schema_privileges()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        await using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync(CancellationToken.None);

        Assert.True(await ScalarAsync(connection, "SELECT has_table_privilege(current_user, 'identity.sessions', 'SELECT')"));
        Assert.True(await ScalarAsync(connection, "SELECT has_table_privilege(current_user, 'security.locks', 'INSERT')"));
        Assert.True(await ScalarAsync(connection, "SELECT has_table_privilege(current_user, 'security.locks', 'UPDATE')"));
        Assert.True(await ScalarAsync(connection, "SELECT has_table_privilege(current_user, 'security.locks', 'DELETE')"));
        Assert.False(await ScalarAsync(connection, "SELECT has_schema_privilege(current_user, 'identity', 'CREATE')"));
        Assert.False(await ScalarAsync(connection, "SELECT has_schema_privilege(current_user, 'security', 'CREATE')"));
        Assert.False(await ScalarAsync(connection, "SELECT rolsuper FROM pg_roles WHERE rolname = current_user"));
    }

    public void Dispose()
    {
        Discard(_institution, _actor.Value);
        Discard(_otherInstitution, _actor.Value);
    }

    private async Task<SessionDetailsV1> RegisterSessionAsync(string providerReference, ActorId? actorId = null)
    {
        ActorId actor = actorId ?? _actor;
        if (actor != _actor)
        {
            SeedActiveIdentity(_institution, actor.Value);
        }

        await using IdentityAccessDbContext context = CreateContext(_institution);
        SessionDetailsV1 details = await CreateService(context, _time).RegisterProviderSessionAsync(
            _institution,
            actor.Value,
            providerReference,
            "device-" + providerReference,
            AuthenticationStrength.SingleFactor,
            _time.GetUtcNow().AddHours(1),
            CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(1));
        return details;
    }

    private static SessionSecurityService CreateService(IdentityAccessDbContext context, TimeProvider timeProvider) =>
        new(context, new IdempotencyService(context, timeProvider), timeProvider);

    private IdentityAccessDbContext CreateContext(InstitutionId? institution)
    {
        AmbientTenantContext tenant = new();
        if (institution is not null)
        {
            tenant.Establish(institution);
        }

        DbContextOptionsBuilder<IdentityAccessDbContext> options = new();
        options.UseNpgsql(_connectionString);

        return new IdentityAccessDbContext(options.Options, tenant);
    }

    private SessionRecord NewSession(string sessionId, string fingerprint, SessionLifecycleState status) => new()
    {
        InstitutionId = _institution.Value,
        SessionId = sessionId,
        IdentityId = _actor.Value,
        ProviderSessionFingerprint = fingerprint,
        AuthenticationStrength = AuthenticationStrength.SingleFactor,
        IssuedAt = _time.GetUtcNow(),
        ExpiresAt = _time.GetUtcNow().AddHours(1),
        LastActivityAt = _time.GetUtcNow(),
        Status = status,
        ConcurrencyVersion = 1L,
    };

    private static async Task<string[]> ColumnNamesAsync(IdentityAccessDbContext context, string schema, string table) =>
        await context.Database
            .SqlQueryRaw<string>(
                "SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = {0} AND table_name = {1} ORDER BY ordinal_position",
                schema,
                table)
            .ToArrayAsync(CancellationToken.None);

    private static async Task<bool> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        return (bool)(await command.ExecuteScalarAsync(CancellationToken.None))!;
    }

    private static async Task<string[]> IndexNamesAsync(IdentityAccessDbContext context, string schema, string table) =>
        await context.Database
            .SqlQueryRaw<string>(
                "SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = {0} AND tablename = {1} ORDER BY indexname",
                schema,
                table)
            .ToArrayAsync(CancellationToken.None);

    private async Task<string[]> CheckConstraintNamesAsync(string qualifiedTable)
    {
        await using NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using NpgsqlCommand command = new(
            "SELECT conname FROM pg_constraint WHERE conrelid = @table::regclass AND contype = 'c' ORDER BY conname",
            connection);
        command.Parameters.AddWithValue("table", qualifiedTable);

        List<string> names = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None))
        {
            names.Add(reader.GetString(0));
        }

        return [.. names];
    }

    private void SeedActiveIdentity(InstitutionId institution, string identityId)
    {
        using IdentityAccessDbContext context = CreateContext(institution);
        DateTimeOffset now = _time.GetUtcNow();

        context.Identities.Add(new IdentityRecord
        {
            InstitutionId = institution.Value,
            IdentityId = identityId,
            ActorType = ActorType.Human,
            IdentityType = IdentityKind.Staff,
            DisplayName = "Session security test actor",
            Status = IdentityState.Active,
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyVersion = 1L,
        });

        context.InstitutionMemberships.Add(new InstitutionMembershipRecord
        {
            InstitutionId = institution.Value,
            MembershipId = Guid.NewGuid().ToString("N"),
            IdentityId = identityId,
            Status = IdentityState.Active,
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyVersion = 1L,
        });

        context.SaveChanges();
    }

    private void Discard(InstitutionId institution, string identityId)
    {
        using IdentityAccessDbContext context = CreateContext(institution);

        context.AuthenticationEvents.ExecuteDelete();
        context.SecurityLocks.ExecuteDelete();
        context.OutboxMessages.ExecuteDelete();
        context.DeadLetters.ExecuteDelete();
        context.IdempotencyRecords.ExecuteDelete();
        context.Sessions.ExecuteDelete();
        context.InstitutionMemberships.ExecuteDelete();
        context.IdentityLinks.ExecuteDelete();
        context.EntitlementVersions.ExecuteDelete();
        context.Identities.Where(identity => identity.IdentityId == identityId).ExecuteDelete();
    }

    /// <summary>A clock the test moves deliberately, so expiry is not a race against the wall clock.</summary>
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
