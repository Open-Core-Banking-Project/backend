using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Application.Devices;
using CoreBanking.IdentityAccess.Domain.Authentication;
using CoreBanking.IdentityAccess.Domain.Identities;
using CoreBanking.IdentityAccess.Infrastructure.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Persistence;

/// <summary>
/// Trusted devices against a real <c>identity_access_db</c>: registration waits for step-up, activation records
/// an effective-dated trust per identity kind, revocation terminates the bound sessions in the same transaction
/// and publishes the facts, and nothing outside the tenant is visible. Decision: P1-E02-D11.
/// </summary>
[Trait("Category", "Database")]
public sealed class TrustedDeviceDatabaseTests : IDisposable
{
    private const string ConnectionStringVariableName = "ConnectionStrings__IdentityAccess";

    private const string Thumbprint = "Zm9vYmFyYmF6cXV1eDEyMzQ1Njc4OTBhYmNkZWZnaGlqa2xtbm9wcXJzdHU";

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("device-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _staff = ActorId.From("device-staff-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _customer = ActorId.From("device-customer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 10, 31, 16, 0, 0, TimeSpan.Zero));

    public TrustedDeviceDatabaseTests()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringVariableName} to the application principal connection string, or run scripts/db-verify.ps1.");
        }

        _connectionString = configured;
        SeedIdentity(_institution, _staff.Value, IdentityKind.Staff);
        SeedIdentity(_institution, _customer.Value, IdentityKind.Customer);
    }

    [Fact]
    public async Task A_registration_waits_for_step_up_and_is_idempotent_per_key()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        TrustedDeviceService devices = CreateService(context);

        TrustedDeviceView started = await devices.StartRegistrationAsync(
            Start(_staff, _staff.Value, "Phone"), CancellationToken.None);
        TrustedDeviceView repeated = await devices.StartRegistrationAsync(
            Start(_staff, _staff.Value, "Phone"), CancellationToken.None);

        Assert.Equal("pending_step_up", started.TrustStateCode);
        Assert.False(started.IsTrustedNow);
        Assert.Equal(started.DeviceId, repeated.DeviceId);
        Assert.Single(await context.TrustedDevices.ToListAsync());

        // Completion without a fresh step-up is refused.
        TrustedDeviceException refused = await Assert.ThrowsAsync<TrustedDeviceException>(() =>
            devices.CompleteRegistrationAsync(
                new CompleteDeviceRegistrationCommand(Actor(_staff, "session-without-step-up", null), started.DeviceId, "correlation"),
                CancellationToken.None));
        Assert.Equal("device_step_up_required", refused.Code);
    }

    [Fact]
    public async Task Completion_trusts_the_device_for_the_privileged_duration()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        TrustedDeviceService devices = CreateService(context);
        TrustedDeviceView started = await devices.StartRegistrationAsync(
            Start(_staff, _staff.Value, "Laptop"), CancellationToken.None);
        const string sessionId = "session-staff-1";
        SeedStepUpEvidence(_institution, _staff, sessionId);

        TrustedDeviceView trusted = await devices.CompleteRegistrationAsync(
            new CompleteDeviceRegistrationCommand(Actor(_staff, sessionId, null), started.DeviceId, "correlation"),
            CancellationToken.None);

        Assert.Equal("trusted", trusted.TrustStateCode);
        Assert.True(trusted.IsTrustedNow);
        Assert.Equal(_time.GetUtcNow().AddDays(30), trusted.TrustExpiresAt);

        TrustedDeviceRecord stored = await context.TrustedDevices.SingleAsync();
        Assert.NotNull(stored.StepUpEvidenceId);
        Assert.Equal(1L, stored.ConcurrencyVersion);

        StepUpEvidenceRecord evidence = await context.StepUpEvidence.SingleAsync();
        Assert.NotNull(evidence.ConsumedAt);
        Assert.Contains(await context.OutboxMessages.ToListAsync(), message => message.EventType == "trusted_device_registered");
    }

    [Fact]
    public async Task A_customer_device_gets_the_standard_duration()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        TrustedDeviceService devices = CreateService(context);
        TrustedDeviceView started = await devices.StartRegistrationAsync(
            Start(_customer, _customer.Value, "Phone"), CancellationToken.None);
        const string sessionId = "session-customer-1";
        SeedStepUpEvidence(_institution, _customer, sessionId);

        TrustedDeviceView trusted = await devices.CompleteRegistrationAsync(
            new CompleteDeviceRegistrationCommand(Actor(_customer, sessionId, null), started.DeviceId, "correlation"),
            CancellationToken.None);

        Assert.Equal(_time.GetUtcNow().AddDays(180), trusted.TrustExpiresAt);
    }

    [Fact]
    public async Task Revoking_a_device_terminates_every_bound_session()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        TrustedDeviceService devices = CreateService(context);
        TrustedDeviceView started = await devices.StartRegistrationAsync(
            Start(_staff, _staff.Value, "Tablet"), CancellationToken.None);
        const string sessionId = "session-staff-2";
        SeedStepUpEvidence(_institution, _staff, sessionId);
        TrustedDeviceView trusted = await devices.CompleteRegistrationAsync(
            new CompleteDeviceRegistrationCommand(Actor(_staff, sessionId, null), started.DeviceId, "correlation"),
            CancellationToken.None);

        SeedSession(_institution, _staff, trusted.DeviceId, "session-bound-1");
        SeedSession(_institution, _staff, trusted.DeviceId, "session-bound-2");

        DeviceRevocationView revocation = await devices.RevokeAsync(
            new RevokeDeviceCommand(Actor(_staff, sessionId, trusted.DeviceId), trusted.DeviceId, "device lost", Administrative: false, "correlation"),
            CancellationToken.None);

        Assert.Equal("revoked", revocation.TrustStateCode);
        Assert.Equal(2, revocation.RevokedSessionCount);
        Assert.False(revocation.Replayed);

        List<SessionRecord> sessions = await context.Sessions
            .Where(session => session.SessionId.StartsWith("session-bound"))
            .ToListAsync();
        Assert.All(sessions, session =>
        {
            Assert.Equal(SessionLifecycleState.Revoked, session.Status);
            Assert.NotNull(session.ProviderRevocationRequestedAt);
            Assert.NotNull(session.RevocationEventId);
        });

        List<string> events = await context.OutboxMessages.Select(message => message.EventType).ToListAsync();
        Assert.Contains("trusted_device_revoked", events);
        Assert.Contains("sessions_revoked_for_device", events);
        Assert.Equal(2, events.Count(eventType => eventType == "session_revoked"));

        // Repeating the revocation is replay, and the device stays revoked.
        DeviceRevocationView replay = await devices.RevokeAsync(
            new RevokeDeviceCommand(Actor(_staff, sessionId, trusted.DeviceId), trusted.DeviceId, "device lost", Administrative: false, "correlation"),
            CancellationToken.None);
        Assert.True(replay.Replayed);
        Assert.Equal("revoked", replay.TrustStateCode);
    }

    [Fact]
    public async Task A_revoked_device_cannot_be_trusted_again()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        TrustedDeviceService devices = CreateService(context);
        TrustedDeviceView started = await devices.StartRegistrationAsync(
            Start(_staff, _staff.Value, "Phone"), CancellationToken.None);
        const string sessionId = "session-staff-3";
        SeedStepUpEvidence(_institution, _staff, sessionId);
        TrustedDeviceView trusted = await devices.CompleteRegistrationAsync(
            new CompleteDeviceRegistrationCommand(Actor(_staff, sessionId, null), started.DeviceId, "correlation"),
            CancellationToken.None);
        await devices.RevokeAsync(
            new RevokeDeviceCommand(Actor(_staff, sessionId, trusted.DeviceId), trusted.DeviceId, "stolen", Administrative: false, "correlation"),
            CancellationToken.None);

        // A new registration of the same key is a new device identity, never the revoked one.
        TrustedDeviceView restarted = await devices.StartRegistrationAsync(
            Start(_staff, _staff.Value, "Phone"), CancellationToken.None);

        Assert.NotEqual(trusted.DeviceId, restarted.DeviceId);
        Assert.Equal("pending_step_up", restarted.TrustStateCode);
        Assert.Equal(2, await context.TrustedDevices.CountAsync());
    }

    [Fact]
    public async Task A_device_may_not_be_managed_for_another_identity()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        TrustedDeviceService devices = CreateService(context);

        TrustedDeviceException denied = await Assert.ThrowsAsync<TrustedDeviceException>(() =>
            devices.StartRegistrationAsync(Start(_staff, _customer.Value, "Someone else"), CancellationToken.None));

        Assert.Equal("device_scope_denied", denied.Code);
    }

    [Fact]
    public async Task Another_institution_sees_no_devices()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        TrustedDeviceService devices = CreateService(context);
        await devices.StartRegistrationAsync(Start(_staff, _staff.Value, "Phone"), CancellationToken.None);

        await using IdentityAccessDbContext other = CreateContext(_otherInstitution);
        TrustedDeviceService otherDevices = CreateService(other);

        Assert.Empty(await otherDevices.ListAsync(
            new ListDevicesQuery(Actor(_staff, "session-other", null), _staff.Value, OtherIdentityRequested: true),
            CancellationToken.None));
    }

    private StartDeviceRegistrationCommand Start(ActorId actorId, string identityId, string displayName) =>
        new(Actor(actorId, "session-start", null), identityId, Thumbprint, DevicePlatform.Ios, displayName, "correlation");

    private AuthenticatedActor Actor(ActorId actorId, string sessionId, string? deviceId) => new()
    {
        Id = actorId,
        Type = ActorType.Human,
        InstitutionId = _institution,
        SessionId = sessionId,
        DeviceId = deviceId,
        EntitlementVersion = 1,
        Strength = AuthenticationStrength.StepUp,
    };

    private TrustedDeviceService CreateService(IdentityAccessDbContext context) =>
        new(context, _time, TrustDurationPolicy.Default);

    public void Dispose()
    {
        Discard(_institution);
        Discard(_otherInstitution);
    }

    /// <summary>
    /// Removes every row this suite created. The outbox relay drains the whole table regardless of tenant, so a
    /// suite that leaves pending rows makes the later relay assertions count them.
    /// </summary>
    private void Discard(InstitutionId institution)
    {
        using IdentityAccessDbContext context = CreateContext(institution);

        context.AuthenticationEvents.ExecuteDelete();
        context.OutboxMessages.ExecuteDelete();
        context.DeadLetters.ExecuteDelete();
        context.StepUpEvidence.ExecuteDelete();
        context.TrustedDevices.ExecuteDelete();
        context.Sessions.ExecuteDelete();
        context.InstitutionMemberships.ExecuteDelete();
        context.Identities.ExecuteDelete();
    }

    private IdentityAccessDbContext CreateContext(InstitutionId institution)
    {
        AmbientTenantContext tenant = new();
        tenant.Establish(institution);

        return new IdentityAccessDbContext(
            new DbContextOptionsBuilder<IdentityAccessDbContext>().UseNpgsql(_connectionString).Options,
            tenant);
    }

    private void SeedStepUpEvidence(InstitutionId institution, ActorId actorId, string sessionId)
    {
        using IdentityAccessDbContext context = CreateContext(institution);
        DateTimeOffset now = _time.GetUtcNow();
        context.StepUpEvidence.Add(new StepUpEvidenceRecord
        {
            InstitutionId = institution.Value,
            EvidenceId = Guid.NewGuid().ToString("N"),
            IdentityId = actorId.Value,
            SessionId = sessionId,
            DeviceId = null,
            Purpose = TrustedDevice.RegistrationPurpose,
            FactorKind = "totp",
            CorrelationId = "correlation",
            IssuedAt = now,
            ExpiresAt = now.AddMinutes(5),
        });
        context.SaveChanges();
    }

    private void SeedSession(InstitutionId institution, ActorId actorId, string deviceId, string sessionId)
    {
        using IdentityAccessDbContext context = CreateContext(institution);
        DateTimeOffset now = _time.GetUtcNow();
        context.Sessions.Add(new SessionRecord
        {
            InstitutionId = institution.Value,
            SessionId = sessionId,
            IdentityId = actorId.Value,
            DeviceId = deviceId,
            ProviderSessionFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sessionId))).ToLowerInvariant(),
            AuthenticationStrength = AuthenticationStrength.MultiFactor,
            IssuedAt = now,
            ExpiresAt = now.AddHours(8),
            LastActivityAt = now,
            Status = SessionLifecycleState.Active,
            ConcurrencyVersion = 1L,
        });
        context.SaveChanges();
    }

    private void SeedIdentity(InstitutionId institution, string identityId, IdentityKind kind)
    {
        using IdentityAccessDbContext context = CreateContext(institution);
        DateTimeOffset now = _time.GetUtcNow();
        context.Identities.Add(new IdentityRecord
        {
            InstitutionId = institution.Value,
            IdentityId = identityId,
            ActorType = ActorType.Human,
            IdentityType = kind,
            DisplayName = "Trusted device test actor",
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

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
