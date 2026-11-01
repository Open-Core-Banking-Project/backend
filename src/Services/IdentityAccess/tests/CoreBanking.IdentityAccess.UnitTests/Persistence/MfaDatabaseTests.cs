using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Application.Authentication;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Domain.Authentication;
using CoreBanking.IdentityAccess.Infrastructure.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Persistence;

/// <summary>
/// Enrolment and step-up evidence against a real <c>identity_access_db</c>, as the application
/// principal. Excluded from the default run and executed by <c>scripts/db-verify.ps1</c> after the
/// migrations are applied. Decision: ADR-0004, ADR-0008, P1-E02-D08.
/// </summary>
[Trait("Category", "Database")]
public sealed class MfaDatabaseTests : IDisposable
{
    private const string ConnectionStringVariableName = "ConnectionStrings__IdentityAccess";
    private const string Purpose = "identity.identity.create";
    private const string ValidCode = "123456";

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("mfa-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("mfa-other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _actor = ActorId.From("mfa-actor-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));

    public MfaDatabaseTests()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringVariableName} to the application principal connection string, or run scripts/db-verify.ps1.");
        }

        _connectionString = configured;
    }

    [Fact]
    public async Task Enrolment_starts_pending_and_the_first_verified_challenge_activates_it()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        MfaService service = CreateService(context, new StubVerifier(ValidCode));

        MfaEnrolmentResultV1 enrolment = await service.EnrolAsync(
            _institution, _actor, MfaFactorKind.Totp, "provider-reference-1", CancellationToken.None);

        Assert.Equal(MfaContractV1.StatusPending, enrolment.Status);

        StepUpChallengeResultV1 result = await service.VerifyStepUpAsync(
            _institution, _actor, "session-1", "device-1", Purpose, ValidCode, "correlation-1", CancellationToken.None);

        Assert.Equal(MfaContractV1.StepUpVerified, result.Status);
        Assert.Equal(_time.GetUtcNow().Add(MfaPolicy.StepUpValidity), result.ExpiresAt);

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        Assert.Equal(MfaContractV1.StatusActive, (await verification.MfaEnrolments.SingleAsync()).Status);
    }

    [Fact]
    public async Task An_unavailable_factor_family_is_refused()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        MfaService service = CreateService(context, new StubVerifier(ValidCode));

        MfaException exception = await Assert.ThrowsAsync<MfaException>(() => service.EnrolAsync(
            _institution, _actor, MfaFactorKind.Passkey, "provider-reference-2", CancellationToken.None));

        Assert.Equal(MfaContractV1.FactorNotAvailable, exception.Code);
    }

    [Fact]
    public async Task A_second_enrolment_for_the_same_factor_is_refused()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        MfaService service = CreateService(context, new StubVerifier(ValidCode));

        await service.EnrolAsync(_institution, _actor, MfaFactorKind.Totp, "provider-reference-3", CancellationToken.None);

        MfaException exception = await Assert.ThrowsAsync<MfaException>(() => service.EnrolAsync(
            _institution, _actor, MfaFactorKind.Totp, "provider-reference-4", CancellationToken.None));

        Assert.Equal(MfaContractV1.FactorInvalid, exception.Code);
    }

    [Fact]
    public async Task Rejected_response_writes_no_evidence_and_does_not_activate_the_enrolment()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        MfaService service = CreateService(context, new StubVerifier(ValidCode));
        await service.EnrolAsync(_institution, _actor, MfaFactorKind.Totp, "provider-reference-5", CancellationToken.None);

        StepUpChallengeResultV1 result = await service.VerifyStepUpAsync(
            _institution, _actor, "session-1", null, Purpose, "000000", "correlation-2", CancellationToken.None);

        Assert.Equal(MfaContractV1.StepUpRejected, result.Status);

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        Assert.Empty(verification.StepUpEvidence);
        Assert.Equal(MfaContractV1.StatusPending, (await verification.MfaEnrolments.SingleAsync()).Status);
    }

    [Fact]
    public async Task Evidence_is_purpose_bound_and_single_use()
    {
        AuthenticatedActor actor = await IssueEvidenceAsync("session-1", "device-1");

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            Assert.False(await CreateService(context, new StubVerifier(ValidCode))
                .TryConsumeAsync(actor, "identity.identity.update", CancellationToken.None));
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            Assert.True(await CreateService(context, new StubVerifier(ValidCode))
                .TryConsumeAsync(actor, Purpose, CancellationToken.None));
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            Assert.False(await CreateService(context, new StubVerifier(ValidCode))
                .TryConsumeAsync(actor, Purpose, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Evidence_expires_after_five_minutes()
    {
        AuthenticatedActor actor = await IssueEvidenceAsync("session-1", "device-1");

        _time.Advance(TimeSpan.FromMinutes(5));

        await using IdentityAccessDbContext context = CreateContext(_institution);
        Assert.False(await CreateService(context, new StubVerifier(ValidCode))
            .TryConsumeAsync(actor, Purpose, CancellationToken.None));
    }

    [Fact]
    public async Task Evidence_is_bound_to_the_issuing_session_and_device()
    {
        AuthenticatedActor actor = await IssueEvidenceAsync("session-1", "device-1");

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            Assert.False(await CreateService(context, new StubVerifier(ValidCode))
                .TryConsumeAsync(actor with { SessionId = "session-2" }, Purpose, CancellationToken.None));
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            Assert.False(await CreateService(context, new StubVerifier(ValidCode))
                .TryConsumeAsync(actor with { DeviceId = "device-2" }, Purpose, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Evidence_from_another_institution_cannot_be_consumed()
    {
        AuthenticatedActor actor = await IssueEvidenceAsync("session-1", "device-1");

        await using IdentityAccessDbContext context = CreateContext(_otherInstitution);
        Assert.False(await CreateService(context, new StubVerifier(ValidCode))
            .TryConsumeAsync(actor with { InstitutionId = _otherInstitution }, Purpose, CancellationToken.None));
    }

    public void Dispose()
    {
        using IdentityAccessDbContext context = CreateContext(_institution);
        context.StepUpEvidence.ExecuteDelete();
        context.MfaEnrolments.ExecuteDelete();
    }

    private async Task<AuthenticatedActor> IssueEvidenceAsync(string sessionId, string? deviceId)
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        MfaService service = CreateService(context, new StubVerifier(ValidCode));
        await service.EnrolAsync(_institution, _actor, MfaFactorKind.Totp, "provider-reference-" + sessionId, CancellationToken.None);
        await service.VerifyStepUpAsync(
            _institution, _actor, sessionId, deviceId, Purpose, ValidCode, "correlation-evidence", CancellationToken.None);

        return new AuthenticatedActor
        {
            Id = _actor,
            Type = ActorType.Human,
            InstitutionId = _institution,
            SessionId = sessionId,
            DeviceId = deviceId,
            EntitlementVersion = 1,
            Strength = AuthenticationStrength.MultiFactor,
        };
    }

    private MfaService CreateService(IdentityAccessDbContext context, IMfaFactorVerifier verifier) =>
        new(context, verifier, _time);

    private IdentityAccessDbContext CreateContext(InstitutionId institution)
    {
        AmbientTenantContext tenant = new();
        tenant.Establish(institution);

        DbContextOptionsBuilder<IdentityAccessDbContext> options = new();
        options.UseNpgsql(_connectionString);

        return new IdentityAccessDbContext(options.Options, tenant);
    }

    private sealed class StubVerifier : IMfaFactorVerifier
    {
        private readonly string _code;

        public StubVerifier(string code) => _code = code;

        public string ProviderName => "stub";

        public Task<MfaVerificationResult> VerifyAsync(MfaVerificationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(request.Response, _code, StringComparison.Ordinal)
                ? MfaVerificationResult.Verified()
                : MfaVerificationResult.Rejected("factor_invalid"));
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
