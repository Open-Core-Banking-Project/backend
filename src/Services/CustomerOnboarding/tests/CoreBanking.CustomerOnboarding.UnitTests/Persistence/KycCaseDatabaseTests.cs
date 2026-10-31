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
using CoreBanking.CustomerOnboarding.Infrastructure.Kyc;
using CoreBanking.CustomerOnboarding.Infrastructure.Onboarding;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using CoreBanking.CustomerOnboarding.Infrastructure.Protection;
using CoreBanking.CustomerOnboarding.Infrastructure.Requirements;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Persistence;

/// <summary>
/// KYC cases, requirements and canonical verifications against a real <c>customer_onboarding_db</c>: the
/// case is opened from the submitted revision's snapshot, an outcome needs a mapping version, uncertain is
/// not a decision, only a reviewer resolves review items and one active verification exists per requirement.
/// Decision: P1-E03-D06.
/// </summary>
[Trait("Category", "Database")]
public sealed class KycCaseDatabaseTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 29, 13, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("kyc-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _officer = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _reviewer = ActorId.From("kyc-reviewer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public KycCaseDatabaseTests()
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
    public async Task A_case_opens_from_the_submitted_revision_snapshot()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseService kyc = new(context, _time);

        (string applicationId, int revision) = await SubmittedAsync(context, "KYC-1");
        KycCaseView opened = await OpenAsync(kyc, applicationId, revision);

        Assert.Equal(KycCaseStatus.Open, opened.Status);
        Assert.Equal(2, opened.Requirements.Count);
        Assert.All(opened.Requirements, requirement => Assert.Equal(KycRequirementStatus.Required, requirement.Status));

        RequirementSnapshotRecord snapshot = await context.RequirementSnapshots.SingleAsync();
        Assert.Equal(snapshot.RequirementSnapshotId, opened.RequirementSnapshotId);
        Assert.Equal(2, await context.KycRequirementHistory.CountAsync());

        OnboardingApplicationConflictException duplicate = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => OpenAsync(kyc, applicationId, revision));
        Assert.Equal("kyc_case_exists", duplicate.Code);
    }

    [Fact]
    public async Task A_case_requires_a_submitted_application_at_the_expected_revision()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);
        KycCaseService kyc = new(context, _time);
        await EnsureRequirementPackAsync(context);

        OnboardingApplicationView draft = await onboarding.StartAsync(
            new StartIndividualApplicationCommand(_institution, _officer, "idem-start-KYC-2", "KYC-2", "branch-1", CompleteCapture(), "correlation"),
            CancellationToken.None);

        OnboardingApplicationConflictException notSubmitted = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => kyc.OpenAsync(
                new OpenKycCaseCommand(_institution, _reviewer, draft.ApplicationId, draft.Revision, "correlation"),
                CancellationToken.None));
        Assert.Equal("kyc_application_not_submitted", notSubmitted.Code);

        OnboardingApplicationView submitted = await onboarding.SubmitAsync(
            new SubmitOnboardingApplicationCommand(_institution, _officer, "idem-submit-KYC-2", draft.ApplicationId, draft.Revision, "correlation"),
            CancellationToken.None);

        OnboardingApplicationConflictException wrongRevision = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => kyc.OpenAsync(
                new OpenKycCaseCommand(_institution, _reviewer, submitted.ApplicationId, submitted.Revision - 1, "correlation"),
                CancellationToken.None));
        Assert.Equal("onboarding_revision_conflict", wrongRevision.Code);
    }

    [Fact]
    public async Task A_succeeded_verification_passes_the_requirement_and_keeps_the_history()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseService kyc = new(context, _time);

        (string applicationId, int revision) = await SubmittedAsync(context, "KYC-3");
        KycCaseView opened = await OpenAsync(kyc, applicationId, revision);

        VerificationView requested = await kyc.RequestVerificationAsync(
            new RequestVerificationCommand(_institution, _reviewer, opened.CaseId, "contact_detail", "document_check", "correlation"),
            CancellationToken.None);

        Assert.Equal(VerificationStatus.Created, requested.Status);
        Assert.Equal(opened.CaseId, requested.CaseId);

        KycCaseView inProgress = await kyc.GetAsync(new GetKycCaseQuery(_institution, _reviewer, opened.CaseId), CancellationToken.None);
        KycRequirementView requirement = Assert.Single(inProgress.Requirements, entry => entry.Code == "contact_detail");
        Assert.Equal(KycRequirementStatus.InProgress, requirement.Status);
        Assert.Equal(requested.VerificationId, requirement.LinkedVerificationId);

        await kyc.RecordAttemptAsync(
            new RecordVerificationAttemptCommand(_institution, _reviewer, requested.VerificationId, "attempt-1", "provider-ref-1", null, "correlation"),
            CancellationToken.None);

        VerificationView succeeded = await kyc.ApplyAttemptResultAsync(
            new ApplyVerificationAttemptResultCommand(
                _institution,
                _reviewer,
                requested.VerificationId,
                "attempt-1",
                VerificationAttemptStatus.Succeeded,
                CanonicalVerificationOutcome.Passed,
                "mapping-v1",
                "evidence-ref-1",
                new string('a', 64),
                "correlation"),
            CancellationToken.None);

        Assert.Equal(VerificationStatus.Succeeded, succeeded.Status);
        Assert.Equal("passed", succeeded.OutcomeCode);
        Assert.Equal("mapping-v1", succeeded.ProviderMappingVersion);

        KycCaseView afterOutcome = await kyc.GetAsync(new GetKycCaseQuery(_institution, _reviewer, opened.CaseId), CancellationToken.None);
        Assert.Equal(
            KycRequirementStatus.Passed,
            Assert.Single(afterOutcome.Requirements, entry => entry.Code == "contact_detail").Status);
        Assert.Equal(KycCaseStatus.Open, afterOutcome.Status);

        List<KycRequirementHistoryRecord> history = await context.KycRequirementHistory
            .Where(record => record.RequirementCode == "contact_detail")
            .OrderBy(record => record.Sequence)
            .ToListAsync();
        Assert.Equal(["required", "in_progress", "passed"], history.Select(record => record.Status));

        List<VerificationStatusHistoryRecord> verificationHistory = await context.VerificationStatusHistory
            .OrderBy(record => record.Sequence)
            .ToListAsync();
        Assert.Equal(["created", "submitted", "succeeded"], verificationHistory.Select(record => record.Status));

        VerificationAttemptRecord attempt = await context.VerificationAttempts.SingleAsync();
        Assert.Equal("succeeded", attempt.Status);
        Assert.NotNull(attempt.ResolvedAt);
    }

    [Fact]
    public async Task A_succeeded_attempt_without_a_mapping_version_is_refused()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseService kyc = new(context, _time);
        VerificationView requested = await RequestVerificationAsync(kyc, context, "KYC-4");

        await kyc.RecordAttemptAsync(
            new RecordVerificationAttemptCommand(_institution, _reviewer, requested.VerificationId, "attempt-1", null, null, "correlation"),
            CancellationToken.None);

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => kyc.ApplyAttemptResultAsync(
                new ApplyVerificationAttemptResultCommand(
                    _institution,
                    _reviewer,
                    requested.VerificationId,
                    "attempt-1",
                    VerificationAttemptStatus.Succeeded,
                    CanonicalVerificationOutcome.Passed,
                    null,
                    null,
                    null,
                    "correlation"),
                CancellationToken.None));

        Assert.Equal("kyc_mapping_version_required", conflict.Code);
        IdentityVerificationRecord stored = await context.IdentityVerifications.SingleAsync();
        Assert.Equal("submitted", stored.Status);
        Assert.Null(stored.Outcome);
    }

    [Fact]
    public async Task An_uncertain_result_leaves_the_requirement_in_progress()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseService kyc = new(context, _time);
        VerificationView requested = await RequestVerificationAsync(kyc, context, "KYC-5");

        await kyc.RecordAttemptAsync(
            new RecordVerificationAttemptCommand(_institution, _reviewer, requested.VerificationId, "attempt-1", null, null, "correlation"),
            CancellationToken.None);

        VerificationView uncertain = await kyc.ApplyAttemptResultAsync(
            new ApplyVerificationAttemptResultCommand(
                _institution,
                _reviewer,
                requested.VerificationId,
                "attempt-1",
                VerificationAttemptStatus.Uncertain,
                null,
                null,
                null,                null,                "correlation"),
            CancellationToken.None);

        Assert.Equal(VerificationStatus.Uncertain, uncertain.Status);
        Assert.Null(uncertain.Outcome);

        KycCaseView caseView = await kyc.GetAsync(new GetKycCaseQuery(_institution, _reviewer, requested.CaseId), CancellationToken.None);
        Assert.Equal(
            KycRequirementStatus.InProgress,
            Assert.Single(caseView.Requirements, entry => entry.Code == "contact_detail").Status);
    }

    [Fact]
    public async Task A_reviewer_resolves_an_inconclusive_requirement_with_a_reason()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseService kyc = new(context, _time);
        VerificationView requested = await RequestVerificationAsync(kyc, context, "KYC-6");

        await kyc.RecordAttemptAsync(
            new RecordVerificationAttemptCommand(_institution, _reviewer, requested.VerificationId, "attempt-1", null, null, "correlation"),
            CancellationToken.None);
        await kyc.ApplyAttemptResultAsync(
            new ApplyVerificationAttemptResultCommand(
                _institution,
                _reviewer,
                requested.VerificationId,
                "attempt-1",
                VerificationAttemptStatus.Succeeded,
                CanonicalVerificationOutcome.Inconclusive,
                "mapping-v1",
                "evidence-ref-1",
                new string('a', 64),
                "correlation"),
            CancellationToken.None);

        KycCaseView inconclusive = await kyc.GetAsync(new GetKycCaseQuery(_institution, _reviewer, requested.CaseId), CancellationToken.None);
        Assert.Equal(
            KycRequirementStatus.Inconclusive,
            Assert.Single(inconclusive.Requirements, entry => entry.Code == "contact_detail").Status);

        OnboardingApplicationConflictException missingReason = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => kyc.ResolveRequirementAsync(
                new ResolveKycRequirementCommand(
                    _institution,
                    _reviewer,
                    requested.CaseId,
                    "contact_detail",
                    CanonicalVerificationOutcome.Passed,
                    "  ",
                    "correlation"),
                CancellationToken.None));
        Assert.Equal("kyc_review_reason_required", missingReason.Code);

        KycCaseView resolved = await kyc.ResolveRequirementAsync(
            new ResolveKycRequirementCommand(
                _institution,
                _reviewer,
                requested.CaseId,
                "contact_detail",
                CanonicalVerificationOutcome.Passed,
                "document verified manually against the uploaded copy",
                "correlation"),
            CancellationToken.None);

        Assert.Equal(
            KycRequirementStatus.Passed,
            Assert.Single(resolved.Requirements, entry => entry.Code == "contact_detail").Status);
        KycRequirementHistoryRecord last = await context.KycRequirementHistory
            .Where(record => record.RequirementCode == "contact_detail")
            .OrderByDescending(record => record.Sequence)
            .FirstAsync();
        Assert.Equal("passed", last.Status);
        Assert.Equal("document verified manually against the uploaded copy", last.Reason);
    }

    [Fact]
    public async Task A_waiver_of_the_last_requirement_completes_the_case()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseService kyc = new(context, _time);

        (string applicationId, int revision) = await SubmittedAsync(context, "KYC-7");
        KycCaseView opened = await OpenAsync(kyc, applicationId, revision);

        VerificationView requested = await kyc.RequestVerificationAsync(
            new RequestVerificationCommand(_institution, _reviewer, opened.CaseId, "contact_detail", "document_check", "correlation"),
            CancellationToken.None);
        await kyc.RecordAttemptAsync(
            new RecordVerificationAttemptCommand(_institution, _reviewer, requested.VerificationId, "attempt-1", null, null, "correlation"),
            CancellationToken.None);
        await kyc.ApplyAttemptResultAsync(
            new ApplyVerificationAttemptResultCommand(
                _institution,
                _reviewer,
                requested.VerificationId,
                "attempt-1",
                VerificationAttemptStatus.Succeeded,
                CanonicalVerificationOutcome.Passed,
                "mapping-v1",
                "evidence-ref-1",
                new string('a', 64),
                "correlation"),
            CancellationToken.None);

        KycCaseView completed = await kyc.WaiveRequirementAsync(
            new WaiveKycRequirementCommand(
                _institution,
                _reviewer,
                opened.CaseId,
                "address_detail",
                "channel does not support address evidence in this release",
                "correlation"),
            CancellationToken.None);

        Assert.Equal(KycCaseStatus.Completed, completed.Status);
        Assert.NotNull(completed.CompletedAt);
        KycRequirementView waived = Assert.Single(completed.Requirements, entry => entry.Code == "address_detail");
        Assert.Equal(KycRequirementStatus.Waived, waived.Status);
        Assert.Equal(_reviewer.Value, waived.WaiverAuthority);

        OnboardingApplicationConflictException closed = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => kyc.ResolveRequirementAsync(
                new ResolveKycRequirementCommand(
                    _institution,
                    _reviewer,
                    opened.CaseId,
                    "contact_detail",
                    CanonicalVerificationOutcome.Failed,
                    "late change",
                    "correlation"),
                CancellationToken.None));
        Assert.Equal("kyc_case_completed", closed.Code);
    }

    [Fact]
    public async Task A_requirement_cannot_start_a_second_verification()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseService kyc = new(context, _time);

        (string applicationId, int revision) = await SubmittedAsync(context, "KYC-8");
        KycCaseView opened = await OpenAsync(kyc, applicationId, revision);

        await kyc.RequestVerificationAsync(
            new RequestVerificationCommand(_institution, _reviewer, opened.CaseId, "contact_detail", "document_check", "correlation"),
            CancellationToken.None);

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => kyc.RequestVerificationAsync(
                new RequestVerificationCommand(_institution, _reviewer, opened.CaseId, "contact_detail", "document_check", "correlation"),
                CancellationToken.None));

        Assert.Equal("kyc_requirement_transition_invalid", conflict.Code);
        Assert.Single(await context.IdentityVerifications.ToListAsync());
    }

    [Fact]
    public async Task The_database_allows_only_one_active_verification_row_per_requirement()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseService kyc = new(context, _time);
        VerificationView requested = await RequestVerificationAsync(kyc, context, "KYC-10");

        IdentityVerificationRecord existing = await context.IdentityVerifications.SingleAsync();
        context.IdentityVerifications.Add(new IdentityVerificationRecord
        {
            InstitutionId = _institution.Value,
            VerificationId = Guid.NewGuid().ToString("N"),
            CaseId = existing.CaseId,
            ApplicationId = existing.ApplicationId,
            PartyId = existing.PartyId,
            RequirementCode = existing.RequirementCode,
            CheckCode = existing.CheckCode,
            RequirementSnapshotId = existing.RequirementSnapshotId,
            Status = "created",
            CreatedAt = Start,
            UpdatedAt = Start,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(requested.CaseId, existing.CaseId);
    }

    [Fact]
    public async Task Another_institution_cannot_read_the_case()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        KycCaseService kyc = new(context, _time);

        (string applicationId, int revision) = await SubmittedAsync(context, "KYC-9");
        KycCaseView opened = await OpenAsync(kyc, applicationId, revision);

        await using CustomerOnboardingDbContext otherContext = CreateContext(_otherInstitution);
        KycCaseService other = new(otherContext, _time);

        await Assert.ThrowsAsync<KycCaseNotFoundException>(
            () => other.GetAsync(new GetKycCaseQuery(_otherInstitution, _reviewer, opened.CaseId), CancellationToken.None));

        await Assert.ThrowsAsync<KycCaseNotFoundException>(
            () => other.RequestVerificationAsync(
                new RequestVerificationCommand(_otherInstitution, _reviewer, opened.CaseId, "contact_detail", "document_check", "correlation"),
                CancellationToken.None));
    }

    private async Task<VerificationView> RequestVerificationAsync(
        KycCaseService kyc,
        CustomerOnboardingDbContext context,
        string reference)
    {
        (string applicationId, int revision) = await SubmittedAsync(context, reference);
        KycCaseView opened = await OpenAsync(kyc, applicationId, revision);

        return await kyc.RequestVerificationAsync(
            new RequestVerificationCommand(_institution, _reviewer, opened.CaseId, "contact_detail", "document_check", "correlation"),
            CancellationToken.None);
    }

    private async Task<KycCaseView> OpenAsync(KycCaseService kyc, string applicationId, int revision) =>
        await kyc.OpenAsync(
            new OpenKycCaseCommand(_institution, _reviewer, applicationId, revision, "correlation"),
            CancellationToken.None);

    private async Task<(string ApplicationId, int Revision)> SubmittedAsync(
        CustomerOnboardingDbContext context,
        string reference)
    {
        await EnsureRequirementPackAsync(context);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);

        OnboardingApplicationView started = await onboarding.StartAsync(
            new StartIndividualApplicationCommand(_institution, _officer, "idem-start-" + reference, reference, "branch-1", CompleteCapture(), "correlation"),
            CancellationToken.None);

        OnboardingApplicationView submitted = await onboarding.SubmitAsync(
            new SubmitOnboardingApplicationCommand(_institution, _officer, "idem-submit-" + reference, started.ApplicationId, started.Revision, "correlation"),
            CancellationToken.None);

        return (submitted.ApplicationId, submitted.Revision);
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
