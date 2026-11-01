using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Application.Authentication;
using CoreBanking.IdentityAccess.Application.Authorisation;
using CoreBanking.IdentityAccess.Application.Devices;
using CoreBanking.IdentityAccess.Application.Permissions;
using CoreBanking.IdentityAccess.Application.Security;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Domain.Authentication;
using CoreBanking.IdentityAccess.Domain.Identities;
using CoreBanking.IdentityAccess.Domain.Security;
using CoreBanking.IdentityAccess.Infrastructure.Access;
using CoreBanking.IdentityAccess.Infrastructure.Authentication;
using CoreBanking.IdentityAccess.Infrastructure.Authorisation;
using CoreBanking.IdentityAccess.Infrastructure.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Access;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Platform;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using CoreBanking.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Persistence;

/// <summary>
/// End-to-end acceptance evidence for slice 15. One staff identity is registered, activated, signed in, enrols a
/// factor, trusts a device with purpose-bound step-up, completes a second step-up from a protected payload envelope
/// and is revoked with its bound session; a stale entitlement version fails closed and another institution reaches
/// nothing. Requirements: IAM-001..IAM-011, US-005..US-009.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IdentityAccessEndToEndTests : IDisposable
{
    private const string ConnectionStringVariableName = "ConnectionStrings__IdentityAccess";
    private const string AcceptanceCode = "123456";

    private static readonly DateTimeOffset Start = new(2026, 10, 31, 19, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("accept-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("acceptother-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _administrator = ActorId.From("accept-admin-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly TestTimeProvider _time = new(Start);
    private readonly RSA _wrappingKey = RSA.Create(2048);
    private readonly string _providerReference = "accept-wrapping-" + Guid.NewGuid().ToString("N");

    public IdentityAccessEndToEndTests()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringVariableName} to the application principal connection string, or run scripts/db-verify.ps1.");
        }

        _connectionString = configured;
        SeedInstitutionBoundary(_institution);
        SeedInstitutionBoundary(_otherInstitution);
    }

    public void Dispose()
    {
        Discard(_institution);
        Discard(_otherInstitution);
        _wrappingKey.Dispose();
    }

    [Fact]
    public async Task A_staff_identity_is_registered_activated_trusted_and_revoked_with_its_session()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        IdentityLifecycleService lifecycle = new(context, new IdempotencyService(context, _time), _time);

        IdentityRegistrationResultV1 registered = await lifecycle.RegisterAsync(
            _institution,
            _administrator,
            "registration-1",
            new RegisterIdentityV1("staff", "Acceptance staff", null),
            "correlation-register",
            CancellationToken.None);

        Assert.Equal("pending", registered.Status);
        Assert.False(registered.ProviderLinked);

        IdentityRecord identity = await context.Identities.SingleAsync(record => record.IdentityId == registered.IdentityId);
        IdentityStatusChangeResultV1 activated = await lifecycle.ChangeStatusAsync(
            _institution,
            _administrator,
            registered.IdentityId,
            "activation-1",
            new ChangeIdentityStatusV1("active", "onboarding complete", identity.ConcurrencyVersion),
            "correlation-activate",
            CancellationToken.None);

        Assert.Equal("active", activated.Status);
        Assert.Equal(2L, activated.EntitlementVersion);

        // A registered identity is visible inside its institution and invisible to another tenant.
        IdentityDetailsV1? found = await lifecycle.FindAsync(_institution, registered.IdentityId, CancellationToken.None);
        Assert.NotNull(found);
        await using IdentityAccessDbContext otherContext = CreateContext(_otherInstitution);
        IdentityLifecycleService otherLifecycle = new(otherContext, new IdempotencyService(otherContext, _time), _time);
        Assert.Null(await otherLifecycle.FindAsync(_otherInstitution, registered.IdentityId, CancellationToken.None));

        SessionDetailsV1 session = await CreateSessionService(context).RegisterProviderSessionAsync(
            _institution,
            registered.IdentityId,
            "provider-session-1",
            deviceId: null,
            AuthenticationStrength.SingleFactor,
            _time.GetUtcNow().AddHours(1),
            CancellationToken.None);

        MfaService mfa = new(context, new SyntheticMfaFactorVerifier(AcceptanceCode), _time);
        MfaEnrolmentResultV1 enrolment = await mfa.EnrolAsync(
            _institution, ActorId.From(registered.IdentityId), MfaFactorKind.Totp, "totp-reference-1", CancellationToken.None);
        Assert.Equal("pending", enrolment.Status);

        // Step 1: purpose-bound step-up evidence, consumed by the device registration in the same instance.
        StepUpChallengeResultV1 stepUp = await mfa.VerifyStepUpAsync(
            _institution,
            ActorId.From(registered.IdentityId),
            session.SessionId,
            deviceId: null,
            TrustedDevice.RegistrationPurpose,
            AcceptanceCode,
            "correlation-step-up",
            CancellationToken.None);
        Assert.Equal("verified", stepUp.Status);

        TrustedDeviceService devices = new(context, _time, TrustDurationPolicy.Default);
        AuthenticatedActor actor = new()
        {
            Id = ActorId.From(registered.IdentityId),
            Type = ActorType.Human,
            InstitutionId = _institution,
            SessionId = session.SessionId,
            EntitlementVersion = activated.EntitlementVersion,
            Strength = AuthenticationStrength.StepUp,
        };

        TrustedDeviceView started = await devices.StartRegistrationAsync(
            new StartDeviceRegistrationCommand(
                actor, registered.IdentityId, Thumbprint(), DevicePlatform.Ios, "Acceptance phone", "correlation-device"),
            CancellationToken.None);
        TrustedDeviceView trusted = await devices.CompleteRegistrationAsync(
            new CompleteDeviceRegistrationCommand(actor, started.DeviceId, "correlation-device"),
            CancellationToken.None);

        Assert.Equal("trusted", trusted.TrustStateCode);
        Assert.True(trusted.IsTrustedNow);

        // A later sign-in on the trusted device binds a new session to it; only device-bound sessions are revoked
        // when the device is.
        SessionSecurityService sessions = CreateSessionService(context);
        SessionDetailsV1 boundSession = await sessions.RegisterProviderSessionAsync(
            _institution,
            registered.IdentityId,
            "provider-session-2",
            trusted.DeviceId,
            AuthenticationStrength.SingleFactor,
            _time.GetUtcNow().AddHours(1),
            CancellationToken.None);

        // Step 2: the same factor response arrives inside a protected payload envelope and still verifies.
        PublicKeyMetadataService keys = new(
            context,
            new DevelopmentKeyOperationPort(new MapSecretProvider(_providerReference, _wrappingKey), _time),
            _time);
        PublicKeyView key = await keys.RegisterVersionAsync(
            _institution, "accept-key", KeyPurposes.PayloadWrapping, _providerReference, CancellationToken.None);
        PayloadEnvelopeService envelopes = new(
            context,
            new DevelopmentKeyOperationPort(new MapSecretProvider(_providerReference, _wrappingKey), _time),
            _time);

        ProtectedPayloadEnvelope envelope = Protect(key, AcceptanceCode, PayloadFieldBindings.StepUpResponse);
        byte[] unprotected = await envelopes.UnprotectAsync(
            new PayloadEnvelopeRequest(_institution, registered.IdentityId, session.SessionId, "correlation-envelope", envelope),
            PayloadFieldBindings.StepUpResponse,
            CancellationToken.None);

        Assert.Equal(AcceptanceCode, Encoding.UTF8.GetString(unprotected));

        StepUpChallengeResultV1 protectedStepUp = await mfa.VerifyStepUpAsync(
            _institution,
            ActorId.From(registered.IdentityId),
            session.SessionId,
            trusted.DeviceId,
            "identity.device.manage",
            Encoding.UTF8.GetString(unprotected),
            "correlation-step-up-protected",
            CancellationToken.None);
        Assert.Equal("verified", protectedStepUp.Status);

        PayloadEnvelopeException replay = await Assert.ThrowsAsync<PayloadEnvelopeException>(() =>
            envelopes.UnprotectAsync(
                new PayloadEnvelopeRequest(_institution, registered.IdentityId, session.SessionId, "correlation-envelope", envelope),
                PayloadFieldBindings.StepUpResponse,
                CancellationToken.None));
        Assert.Equal(PayloadEnvelopeCodes.Invalid, replay.Code);

        // Revocation terminates the bound session in the same transaction and publishes both facts.
        DeviceRevocationView revoked = await devices.RevokeAsync(
            new RevokeDeviceCommand(actor, trusted.DeviceId, "lost phone", false, "correlation-revoke"),
            CancellationToken.None);

        Assert.Equal("revoked", revoked.TrustStateCode);
        Assert.Equal(1, revoked.RevokedSessionCount);

        SessionRecord boundRecord = await context.Sessions.SingleAsync(record => record.SessionId == boundSession.SessionId);
        Assert.Equal(SessionLifecycleState.Revoked, boundRecord.Status);

        SessionRecord unboundRecord = await context.Sessions.SingleAsync(record => record.SessionId == session.SessionId);
        Assert.Equal(SessionLifecycleState.Active, unboundRecord.Status);

        List<string> events = await context.OutboxMessages
            .Where(message => message.InstitutionId == _institution.Value)
            .Select(message => message.EventType)
            .ToListAsync();
        Assert.Contains("trusted_device_revoked", events);
        Assert.Contains("sessions_revoked_for_device", events);
        Assert.Contains("identity_registered", events);
        Assert.Contains("identity_status_changed", events);
    }

    [Fact]
    public async Task A_stale_entitlement_version_and_a_missing_grant_both_fail_closed()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        DateTimeOffset now = _time.GetUtcNow();

        context.Roles.Add(new RoleRecord
        {
            InstitutionId = _institution.Value,
            RoleId = "accept-role",
            RoleCode = "accept_role",
            RoleName = "Acceptance role",
            Version = 1,
            Status = RecordStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        });

        string permission = IdentityPermissions.IdentityView.Value;
        context.RolePermissions.Add(new RolePermissionRecord
        {
            InstitutionId = _institution.Value,
            RoleId = "accept-role",
            PermissionCode = permission,
        });

        string identityId = "accept-actor-" + Guid.NewGuid().ToString("N")[..10];
        context.Identities.Add(new IdentityRecord
        {
            InstitutionId = _institution.Value,
            IdentityId = identityId,
            ActorType = ActorType.Human,
            IdentityType = IdentityKind.Staff,
            DisplayName = "Acceptance actor",
            Status = IdentityState.Active,
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyVersion = 1,
        });
        context.IdentityRoles.Add(new IdentityRoleRecord
        {
            InstitutionId = _institution.Value,
            IdentityId = identityId,
            RoleId = "accept-role",
            ValidFrom = now.AddHours(-1),
            AssignedBy = _administrator.Value,
        });
        context.EntitlementVersions.Add(new EntitlementVersionRecord
        {
            InstitutionId = _institution.Value,
            IdentityId = identityId,
            Version = 4,
            UpdatedAt = now,
        });
        await context.SaveChangesAsync(CancellationToken.None);

        RelationalEntitlementStore store = new(context, _time);
        AuthorisationService authorisation = new(
            new AuthorisationPolicy(store, store, _time, TimeSpan.FromSeconds(60)),
            new PersistentAuthorisationAuditWriter(context),
            _time);

        AuthenticatedActor Current(long version) => new()
        {
            Id = ActorId.From(identityId),
            Type = ActorType.Human,
            InstitutionId = _institution,
            SessionId = "accept-session",
            EntitlementVersion = version,
            Strength = AuthenticationStrength.SingleFactor,
        };

        AuthorisationRequest Request(long version) => new()
        {
            Requirement = PermissionRequirement.For(IdentityPermissions.IdentityView),
            Scope = ResourceScope.ForInstitution(_institution),
            Actor = Current(version),
        };

        AuthorisationDecision allowed = await authorisation.AuthoriseAsync(
            Request(4), "identity.identity.view", "correlation-allowed", CancellationToken.None);
        Assert.True(allowed.IsAllowed);

        // A grant change advances the version, so the old snapshot must not keep working.
        EntitlementVersionRecord version = await context.EntitlementVersions.SingleAsync();
        version.Version = 5;
        await context.SaveChangesAsync(CancellationToken.None);

        AuthorisationDecision stale = await authorisation.AuthoriseAsync(
            Request(4), "identity.identity.view", "correlation-stale", CancellationToken.None);
        Assert.False(stale.IsAllowed);

        // Withdrawing the grant is effective for an up-to-date actor at once.
        context.IdentityRoles.RemoveRange(context.IdentityRoles);
        await context.SaveChangesAsync(CancellationToken.None);

        AuthorisationDecision withoutGrant = await authorisation.AuthoriseAsync(
            Request(5), "identity.identity.view", "correlation-nogrant", CancellationToken.None);
        Assert.False(withoutGrant.IsAllowed);
    }

    private const string AcceptanceThumbprint = "Zm9vYmFyYmF6cXV1eDEyMzQ1Njc4OTBhYmNkZWZnaGlqa2xtbm9wcXJzdHU";

    private static string Thumbprint() => AcceptanceThumbprint;

    private SessionSecurityService CreateSessionService(IdentityAccessDbContext context) =>
        new(context, new IdempotencyService(context, _time), _time);

    private ProtectedPayloadEnvelope Protect(PublicKeyView key, string plaintext, PayloadFieldBinding binding)
    {
        byte[] contentKey = RandomNumberGenerator.GetBytes(PayloadEnvelopePolicy.ContentKeyBytes);
        byte[] nonce = RandomNumberGenerator.GetBytes(PayloadEnvelopePolicy.NonceBytes);
        DateTimeOffset issued = _time.GetUtcNow();

        ProtectedPayloadEnvelope envelope = new(
            PayloadEnvelopePolicy.SchemaVersion,
            key.KeyId,
            key.KeyVersion,
            PayloadEnvelopePolicy.Algorithm,
            binding.Purpose,
            binding.Audience,
            binding.Field,
            issued,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            string.Empty,
            Base64Url(nonce),
            string.Empty,
            string.Empty);

        byte[] aad = Encoding.UTF8.GetBytes(PayloadEnvelopePolicy.AdditionalData(envelope));
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[PayloadEnvelopePolicy.TagBytes];
        using (AesGcm aes = new(contentKey, PayloadEnvelopePolicy.TagBytes))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, aad);
        }

        using RSA wrapping = RSA.Create();
        wrapping.ImportFromPem(key.PublicKey);

        return envelope with
        {
            WrappedKey = Base64Url(wrapping.Encrypt(contentKey, RSAEncryptionPadding.OaepSHA256)),
            Ciphertext = Base64Url(ciphertext),
            Tag = Base64Url(tag),
        };
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private void SeedInstitutionBoundary(InstitutionId institution)
    {
        using IdentityAccessDbContext context = CreateContext(institution);
        context.InstitutionBoundaries.Add(new InstitutionBoundaryRecord
        {
            InstitutionId = institution.Value,
            ProvisioningPlanVersion = 1,
            Status = "active",
            SourceEventId = Guid.NewGuid().ToString("N"),
            InitialisedAt = _time.GetUtcNow(),
        });
        context.SaveChanges();
    }

    private IdentityAccessDbContext CreateContext(InstitutionId institution)
    {
        AmbientTenantContext tenant = new();
        tenant.Establish(institution);

        return new IdentityAccessDbContext(
            new DbContextOptionsBuilder<IdentityAccessDbContext>().UseNpgsql(_connectionString).Options,
            tenant);
    }

    private void Discard(InstitutionId institution)
    {
        using IdentityAccessDbContext context = CreateContext(institution);

        context.AuthenticationEvents.ExecuteDelete();
        context.OutboxMessages.ExecuteDelete();
        context.DeadLetters.ExecuteDelete();
        context.IdempotencyRecords.ExecuteDelete();
        context.EnvelopeReplays.ExecuteDelete();
        context.SecurityKeyMetadata.ExecuteDelete();
        context.TrustedDevices.ExecuteDelete();
        context.StepUpEvidence.ExecuteDelete();
        context.MfaEnrolments.ExecuteDelete();
        context.Sessions.ExecuteDelete();
        context.IdentityRoles.ExecuteDelete();
        context.RolePermissions.ExecuteDelete();
        context.Roles.ExecuteDelete();
        context.EntitlementVersions.ExecuteDelete();
        context.InstitutionMemberships.ExecuteDelete();
        context.IdentityLinks.ExecuteDelete();
        context.Identities.ExecuteDelete();
        context.InstitutionBoundaries.ExecuteDelete();
    }

    private sealed class MapSecretProvider : ISecretProvider
    {
        private readonly string _logicalName;
        private readonly RSA _key;

        public MapSecretProvider(string logicalName, RSA key)
        {
            _logicalName = logicalName;
            _key = key;
        }

        public Task<string?> TryReadAsync(string logicalName, CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(logicalName, _logicalName, StringComparison.Ordinal)
                ? _key.ExportPkcs8PrivateKeyPem()
                : null);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
