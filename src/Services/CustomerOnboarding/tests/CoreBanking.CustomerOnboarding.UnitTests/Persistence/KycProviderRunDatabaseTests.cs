using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CustomerOnboarding.Application.Kyc;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Application.Requirements;
using CoreBanking.CustomerOnboarding.Domain.Kyc;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using CoreBanking.CustomerOnboarding.Infrastructure.Integration;
using CoreBanking.CustomerOnboarding.Infrastructure.Kyc;
using CoreBanking.CustomerOnboarding.Infrastructure.Onboarding;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using CoreBanking.CustomerOnboarding.Infrastructure.Protection;
using CoreBanking.CustomerOnboarding.Infrastructure.Requirements;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Persistence;

/// <summary>
/// The synthetic provider run against a real <c>customer_onboarding_db</c>: a refusal before acceptance is
/// retried as a new attempt, a late submission becomes uncertain and is resolved only by status enquiry, an
/// unknown submission stays uncertain, and an accepted outcome records its route, mapping version and evidence
/// reference. Decision: P1-E03-D07, P1-E03-D08.
/// </summary>
[Trait("Category", "Database")]
public sealed class KycProviderRunDatabaseTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("prov-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _officer = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _reviewer = ActorId.From("kyc-reviewer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public KycProviderRunDatabaseTests()
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
    public async Task A_refused_submission_is_retried_as_a_new_attempt()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseView kycCase = await CaseAsync(context, "PROV-1");
        SyntheticKycProviderAdapter provider = new(new SyntheticKycProviderScript
        {
            UnavailableSubmissionsBeforeAccept = 1,
            Outcome = CanonicalVerificationOutcome.Passed,
        });

        KycVerificationRunView run = await RunnerAsync(context, provider).RunAsync(
            RunCommand(kycCase.CaseId), CancellationToken.None);

        Assert.Equal("succeeded", run.StatusCode);
        Assert.Equal(2, run.AttemptCount);
        Assert.False(run.RequiresStatusEnquiry);
        Assert.Equal("passed", run.OutcomeCode);

        List<VerificationAttemptRecord> attempts = await context.VerificationAttempts.ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Contains(attempts, attempt => attempt.Status == "cancelled_before_submission");
        VerificationAttemptRecord accepted = Assert.Single(attempts, attempt => attempt.Status == "succeeded");
        Assert.Equal("synthetic-kyc-route-1", accepted.ProviderRouteId);
    }

    [Fact]
    public async Task A_late_submission_is_uncertain_and_resolved_only_by_status_enquiry()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseView kycCase = await CaseAsync(context, "PROV-2");
        SyntheticKycProviderAdapter provider = new(new SyntheticKycProviderScript
        {
            TimedOutSubmissionsBeforeAccept = 1,
            PendingEnquiriesBeforeResult = 1,
        });
        KycVerificationRunner runner = RunnerAsync(context, provider);

        KycVerificationRunView run = await runner.RunAsync(RunCommand(kycCase.CaseId), CancellationToken.None);

        Assert.Equal("uncertain", run.StatusCode);
        Assert.True(run.RequiresStatusEnquiry);
        Assert.Null(run.OutcomeCode);

        KycCaseView afterRun = await KycAsync(context).GetAsync(
            new GetKycCaseQuery(_institution, _reviewer, kycCase.CaseId), CancellationToken.None);
        KycRequirementView requirement = afterRun.Requirements.Single(candidate => candidate.Code == "contact_detail");
        Assert.NotEqual("passed", requirement.StatusCode);

        KycVerificationRunView resolved = await runner.EnquireAsync(
            new EnquireKycVerificationCommand(_institution, _reviewer, kycCase.CaseId, run.VerificationId, "correlation"),
            CancellationToken.None);

        Assert.Equal("succeeded", resolved.StatusCode);
        Assert.Equal("passed", resolved.OutcomeCode);
        Assert.False(resolved.RequiresStatusEnquiry);
    }

    [Fact]
    public async Task An_accepted_outcome_records_its_route_mapping_and_evidence()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseView kycCase = await CaseAsync(context, "PROV-3");
        SyntheticKycProviderAdapter provider = new(SyntheticKycProviderScript.Passed);

        KycVerificationRunView run = await RunnerAsync(context, provider).RunAsync(
            RunCommand(kycCase.CaseId), CancellationToken.None);

        IdentityVerificationRecord verification = await context.IdentityVerifications
            .SingleAsync(record => record.VerificationId == run.VerificationId);
        Assert.Equal("succeeded", verification.Status);
        Assert.Equal("passed", verification.Outcome);
        Assert.Equal("synthetic-kyc-mapping-v1", verification.ProviderMappingVersion);
        Assert.StartsWith("syn-evidence-", verification.EvidenceReference, StringComparison.Ordinal);
        Assert.Equal(64, verification.EvidenceHash!.Length);
        Assert.Equal(
            SyntheticKycProviderAdapter.EvidenceHashFor(
                _institution.Value,
                run.AttemptId,
                CanonicalVerificationOutcome.Passed),
            verification.EvidenceHash);

        List<string> auditActions = await context.CustomerAuditEvents
            .Select(record => record.Action)
            .ToListAsync();
        Assert.Contains(KycVerificationRunner.ActionAttemptSubmitted, auditActions);
        Assert.Contains(KycVerificationRunner.ActionAttemptEnquired, auditActions);
    }

    [Fact]
    public async Task A_failed_check_records_the_failure_without_evidence()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseView kycCase = await CaseAsync(context, "PROV-4");
        SyntheticKycProviderAdapter provider = new(SyntheticKycProviderScript.Failed);

        KycVerificationRunView run = await RunnerAsync(context, provider).RunAsync(
            RunCommand(kycCase.CaseId), CancellationToken.None);

        Assert.Equal("succeeded", run.StatusCode);
        Assert.Equal("failed", run.OutcomeCode);

        KycCaseView afterRun = await KycAsync(context).GetAsync(
            new GetKycCaseQuery(_institution, _reviewer, kycCase.CaseId), CancellationToken.None);
        KycRequirementView requirement = afterRun.Requirements.Single(candidate => candidate.Code == "contact_detail");
        Assert.Equal("failed", requirement.StatusCode);
    }

    [Fact]
    public async Task An_unknown_submission_stays_uncertain_and_is_never_resubmitted()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseView kycCase = await CaseAsync(context, "PROV-5");

        KycVerificationRunView run = await RunnerAsync(
            context,
            new SyntheticKycProviderAdapter(new SyntheticKycProviderScript { TimedOutSubmissionsBeforeAccept = 1 }))
            .RunAsync(RunCommand(kycCase.CaseId), CancellationToken.None);

        // A second runner with a fresh provider script has no record of the submission, so enquiry answers unknown.
        KycVerificationRunView enquiry = await RunnerAsync(context, new SyntheticKycProviderAdapter(SyntheticKycProviderScript.Passed))
            .EnquireAsync(
                new EnquireKycVerificationCommand(_institution, _reviewer, kycCase.CaseId, run.VerificationId, "correlation"),
                CancellationToken.None);

        Assert.True(enquiry.RequiresStatusEnquiry);
        Assert.Equal("uncertain", enquiry.StatusCode);
        Assert.False(
            await context.IdentityVerifications.AnyAsync(record => record.Status == "succeeded"));
    }

    [Fact]
    public async Task A_waived_requirement_needs_no_provider_run()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseView kycCase = await CaseAsync(context, "PROV-6");
        KycCaseService kyc = KycAsync(context);
        await kyc.WaiveRequirementAsync(
            new WaiveKycRequirementCommand(_institution, _reviewer, kycCase.CaseId, "contact_detail", "not available in this channel", "correlation"),
            CancellationToken.None);

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => RunnerAsync(context, new SyntheticKycProviderAdapter(SyntheticKycProviderScript.Passed))
                .RunAsync(RunCommand(kycCase.CaseId), CancellationToken.None));

        Assert.Equal("kyc_requirement_waived", conflict.Code);
    }

    private RunKycVerificationCommand RunCommand(string caseId) =>
        new(_institution, _reviewer, caseId, "contact_detail", "identity_document_check", "correlation");

    private KycVerificationRunner RunnerAsync(CustomerOnboardingDbContext context, IKycProviderPort provider) =>
        new(context, KycAsync(context), provider, _time, KycVerificationRunOptions.Default);

    private KycCaseService KycAsync(CustomerOnboardingDbContext context) => new(context, _time);

    private async Task<KycCaseView> CaseAsync(CustomerOnboardingDbContext context, string reference)
    {
        await EnsureRequirementPackAsync(context);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);

        OnboardingApplicationView started = await onboarding.StartAsync(
            new StartIndividualApplicationCommand(_institution, _officer, "idem-start-" + reference, reference, "branch-1", CompleteCapture(), "correlation"),
            CancellationToken.None);

        OnboardingApplicationView submitted = await onboarding.SubmitAsync(
            new SubmitOnboardingApplicationCommand(_institution, _officer, "idem-submit-" + reference, started.ApplicationId, started.Revision, "correlation"),
            CancellationToken.None);

        return await KycAsync(context).OpenAsync(
            new OpenKycCaseCommand(_institution, _reviewer, submitted.ApplicationId, submitted.Revision, "correlation"),
            CancellationToken.None);
    }

    private async Task EnsureRequirementPackAsync(CustomerOnboardingDbContext context)
    {
        if (await context.RequirementPacks.AnyAsync(pack => pack.Status == "effective"))
        {
            return;
        }

        RequirementService requirements = new(context, new PendingScanDocumentQuarantine(), _time);
        await requirements.PublishPackAsync(
            new PublishRequirementPackCommand(
                _institution,
                _officer,
                "phase1-test",
                [
                    OnboardingRequirement.Create("contact_detail", "Contact detail", true, ["document"]),
                    OnboardingRequirement.Create("address_detail", "Address detail", true, ["document"]),
                ],
                "correlation"),
            CancellationToken.None);
    }

    private static OnboardingCapture CompleteCapture() =>
        new(
            PersonName.Create("Ada", null, "Lovelace"),
            BirthDetails.Create(new DateOnly(1990, 5, 12), "GB", DateOnly.FromDateTime(Start.UtcDateTime)),
            [ContactPoint.Create(ContactPointType.Email, "ada@example.com")],
            [Address.Create(AddressType.Residential, "12 Analytical Engine Way", null, "London", null, "GB")]);

    private CustomerOnboardingDbContext CreateContext(InstitutionId institution)
    {
        AmbientTenantContext tenant = new();
        tenant.Establish(institution);

        return new CustomerOnboardingDbContext(
            new DbContextOptionsBuilder<CustomerOnboardingDbContext>().UseNpgsql(_connectionString).Options,
            tenant);
    }

    private OnboardingApplicationService CreateOnboardingService(CustomerOnboardingDbContext context) =>
        new(
            context,
            new AesGcmProtectedValueProtector(CreateProtectionOptions()),
            new IdempotencyService(context, _time),
            _time);

    private static ProtectedValueProtectionOptions CreateProtectionOptions() =>
        ProtectedValueProtectionOptions.Create(
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)(32 + index)).ToArray()),
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)(96 + index)).ToArray()));

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
