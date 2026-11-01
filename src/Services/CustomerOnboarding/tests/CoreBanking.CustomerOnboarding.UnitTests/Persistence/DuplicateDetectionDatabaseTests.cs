using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CustomerOnboarding.Application.Duplicates;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Application.Requirements;
using CoreBanking.CustomerOnboarding.Domain.Duplicates;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using CoreBanking.CustomerOnboarding.Infrastructure.Duplicates;
using CoreBanking.CustomerOnboarding.Infrastructure.Onboarding;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using CoreBanking.CustomerOnboarding.Infrastructure.Protection;
using CoreBanking.CustomerOnboarding.Infrastructure.Requirements;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Persistence;

/// <summary>
/// Exact duplicate detection and reviewer decisions against a real <c>customer_onboarding_db</c>: one
/// live candidate set per application revision, masked evidence only, a decision that is final and a
/// refusal when candidate evidence or the set version moved on. Decision: P1-E03-D04, P1-E03-D05.
/// </summary>
[Trait("Category", "Database")]
public sealed class DuplicateDetectionDatabaseTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("dup-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _officer = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _reviewer = ActorId.From("duplicate-reviewer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public DuplicateDetectionDatabaseTests()
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
    public async Task An_exact_contact_match_raises_a_masked_possible_candidate()
    {
        Submitted first = await SubmitAsync(
            "DUP-1-A",
            Capture("Ada", "Lovelace", new DateOnly(1990, 5, 12), "ada@example.com", "12 Analytical Engine Way"));
        Submitted second = await SubmitAsync(
            "DUP-1-B",
            Capture("Grace", "Hopper", new DateOnly(1906, 12, 9), "ada@example.com", "9 Compiler Road"));

        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        DuplicateCandidateSetView view = await DetectAsync(context, second);

        Assert.Equal(second.ApplicationId, view.ApplicationId);
        Assert.Equal(second.Revision, view.ApplicationRevision);
        Assert.Equal(DuplicateCandidateSetStatus.Open, view.Status);
        Assert.Equal("open", view.StatusCode);
        Assert.Equal(1L, view.Version);
        Assert.Equal(DuplicateMatchRules.RuleVersion, view.RuleVersion);

        DuplicateCandidateView candidate = Assert.Single(view.Candidates);
        Assert.Equal(first.PartyId, candidate.CandidatePartyId);
        Assert.Equal("pending", candidate.CandidatePartyStatus);
        Assert.Equal(DuplicateMatchBand.Possible, candidate.Band);
        Assert.Equal("possible", candidate.BandCode);

        DuplicateMatchFactorView factor = Assert.Single(candidate.Factors);
        Assert.Equal("contact_point", factor.KindCode);
        Assert.Equal("email", factor.Basis);

        Assert.Contains(candidate.MaskedMatches, value => value.Contains("***", StringComparison.Ordinal));
        Assert.DoesNotContain(candidate.MaskedMatches, value => value.Contains("ada@example.com", StringComparison.OrdinalIgnoreCase));

        DuplicateCandidateRecord stored = await context.DuplicateCandidates.SingleAsync();
        Assert.DoesNotContain("ada@example.com", stored.Factors, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("possible", stored.Band);
    }

    [Fact]
    public async Task Matching_name_and_birth_date_raises_a_probable_candidate()
    {
        Submitted first = await SubmitAsync(
            "DUP-2-A",
            Capture("Ada", "Lovelace", new DateOnly(1990, 5, 12), "first@example.com", "12 Analytical Engine Way"));
        Submitted second = await SubmitAsync(
            "DUP-2-B",
            Capture("Ada", "Lovelace", new DateOnly(1990, 5, 12), "second@example.com", "44 Difference Road"));

        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        DuplicateCandidateSetView view = await DetectAsync(context, second);

        DuplicateCandidateView candidate = Assert.Single(view.Candidates);
        Assert.Equal(first.PartyId, candidate.CandidatePartyId);
        Assert.Equal(DuplicateMatchBand.Probable, candidate.Band);
        Assert.Contains(candidate.Factors, factor => factor.KindCode == "name_and_birth_date");
        Assert.Contains("birthYear:1990", candidate.MaskedMatches);
    }

    [Fact]
    public async Task The_subject_is_never_its_own_candidate_and_another_institution_is_excluded()
    {
        await SubmitAsync(
            "DUP-3-OTHER",
            Capture("Ada", "Lovelace", new DateOnly(1990, 5, 12), "shared@example.com", "1 Elsewhere Street"),
            _otherInstitution);
        Submitted first = await SubmitAsync(
            "DUP-3-A",
            Capture("Ada", "Lovelace", new DateOnly(1970, 1, 2), "shared@example.com", "2 Local Street"));
        Submitted second = await SubmitAsync(
            "DUP-3-B",
            Capture("Grace", "Hopper", new DateOnly(1906, 12, 9), "shared@example.com", "3 Compiler Road"));

        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        DuplicateCandidateSetView view = await DetectAsync(context, second);

        DuplicateCandidateView candidate = Assert.Single(view.Candidates);
        Assert.Equal(first.PartyId, candidate.CandidatePartyId);
        Assert.DoesNotContain(view.Candidates, entry => entry.CandidatePartyId == second.PartyId);
    }

    [Fact]
    public async Task A_second_run_supersedes_the_open_set_and_keeps_its_evidence()
    {
        await SubmitAsync(
            "DUP-4-A",
            Capture("Ada", "Lovelace", new DateOnly(1990, 5, 12), "duplicate@example.com", "12 Analytical Engine Way"));
        Submitted second = await SubmitAsync(
            "DUP-4-B",
            Capture("Grace", "Hopper", new DateOnly(1906, 12, 9), "duplicate@example.com", "9 Compiler Road"));

        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        DuplicateCandidateSetView firstRun = await DetectAsync(context, second);
        DuplicateCandidateSetView secondRun = await DetectAsync(context, second);

        Assert.NotEqual(firstRun.SetId, secondRun.SetId);

        List<DuplicateCandidateSetRecord> stored = await context.DuplicateCandidateSets
            .Include(set => set.Candidates)
            .ToListAsync();

        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, set => set.Status == "superseded" && set.Version == 2L && set.Candidates.Count == 1);
        Assert.Contains(stored, set => set.Status == "open" && set.Version == 1L && set.Candidates.Count == 1);
    }

    [Fact]
    public async Task A_decision_is_recorded_and_final_and_blocks_a_new_run()
    {
        await SubmitAsync(
            "DUP-5-A",
            Capture("Ada", "Lovelace", new DateOnly(1990, 5, 12), "decided@example.com", "12 Analytical Engine Way"));
        Submitted second = await SubmitAsync(
            "DUP-5-B",
            Capture("Grace", "Hopper", new DateOnly(1906, 12, 9), "decided@example.com", "9 Compiler Road"));

        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        DuplicateCandidateSetView detected = await DetectAsync(context, second);

        DuplicateCandidateSetView decided = await DecideAsync(
            context,
            detected.SetId,
            detected.Version,
            DuplicateReviewDecision.Distinct,
            "shared household email; different people");

        Assert.Equal(DuplicateCandidateSetStatus.Decided, decided.Status);
        Assert.Equal("distinct", decided.DecisionCode);
        Assert.Equal(_reviewer.Value, decided.DecidedBy);
        Assert.Equal(2L, decided.Version);
        Assert.Equal("shared household email; different people", decided.DecisionReason);

        OnboardingApplicationConflictException decideAgain = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => DecideAsync(context, decided.SetId, decided.Version, DuplicateReviewDecision.SamePerson, "second review"));
        Assert.Equal("duplicate_candidate_set_closed", decideAgain.Code);

        OnboardingApplicationConflictException detectAgain = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => DetectAsync(context, second));
        Assert.Equal("duplicate_review_already_decided", detectAgain.Code);
    }

    [Fact]
    public async Task A_stale_candidate_set_version_is_refused()
    {
        await SubmitAsync(
            "DUP-6-A",
            Capture("Ada", "Lovelace", new DateOnly(1990, 5, 12), "stale@example.com", "12 Analytical Engine Way"));
        Submitted second = await SubmitAsync(
            "DUP-6-B",
            Capture("Grace", "Hopper", new DateOnly(1906, 12, 9), "stale@example.com", "9 Compiler Road"));

        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        DuplicateCandidateSetView detected = await DetectAsync(context, second);

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => DecideAsync(context, detected.SetId, detected.Version + 4, DuplicateReviewDecision.Distinct, "stale screen"));

        Assert.Equal("duplicate_candidate_set_stale_version", conflict.Code);
    }

    [Fact]
    public async Task Changed_candidate_evidence_requires_a_new_detection_run()
    {
        Submitted first = await SubmitAsync(
            "DUP-7-A",
            Capture("Ada", "Lovelace", new DateOnly(1990, 5, 12), "changed@example.com", "12 Analytical Engine Way"));
        Submitted second = await SubmitAsync(
            "DUP-7-B",
            Capture("Grace", "Hopper", new DateOnly(1906, 12, 9), "changed@example.com", "9 Compiler Road"));

        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        DuplicateCandidateSetView detected = await DetectAsync(context, second);

        PartyContactRecord contact = await context.PartyContacts.SingleAsync(record => record.PartyId == first.PartyId);
        contact.EffectiveTo = Start.AddMinutes(1);
        await context.SaveChangesAsync();

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => DecideAsync(context, detected.SetId, detected.Version, DuplicateReviewDecision.SamePerson, "decided on old evidence"));

        Assert.Equal("duplicate_candidates_stale", conflict.Code);
    }

    [Fact]
    public async Task Only_one_live_candidate_set_can_exist_per_application_revision()
    {
        await SubmitAsync(
            "DUP-8-A",
            Capture("Ada", "Lovelace", new DateOnly(1990, 5, 12), "live@example.com", "12 Analytical Engine Way"));
        Submitted second = await SubmitAsync(
            "DUP-8-B",
            Capture("Grace", "Hopper", new DateOnly(1906, 12, 9), "live@example.com", "9 Compiler Road"));

        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        await DetectAsync(context, second);

        context.DuplicateCandidateSets.Add(new DuplicateCandidateSetRecord
        {
            InstitutionId = _institution.Value,
            SetId = Guid.NewGuid().ToString("N"),
            ApplicationId = second.ApplicationId,
            ApplicationRevision = second.Revision,
            RuleVersion = DuplicateMatchRules.RuleVersion,
            Status = DuplicateCodes.Open,
            Version = 1L,
            CreatedBy = _reviewer.Value,
            CreatedAt = Start,
            UpdatedAt = Start,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Detection_requires_a_submitted_application_at_the_expected_revision()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);
        await EnsureRequirementPackAsync(context, _institution);

        OnboardingApplicationView draft = await onboarding.StartAsync(
            new StartIndividualApplicationCommand(
                _institution,
                _officer,
                "idem-start-DUP-9",
                "DUP-9",
                "branch-1",
                Capture("Ada", "Lovelace", new DateOnly(1990, 5, 12), "draft@example.com", "12 Analytical Engine Way"),
                "correlation"),
            CancellationToken.None);

        DuplicateDetectionService detection = new(context, _time);

        OnboardingApplicationConflictException notSubmitted = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => detection.DetectAsync(
                new DetectDuplicateCandidatesCommand(_institution, _reviewer, draft.ApplicationId, draft.Revision, "correlation"),
                CancellationToken.None));
        Assert.Equal("duplicate_detection_requires_submitted_application", notSubmitted.Code);

        OnboardingApplicationView submitted = await onboarding.SubmitAsync(
            new SubmitOnboardingApplicationCommand(
                _institution,
                _officer,
                "idem-submit-DUP-9",
                draft.ApplicationId,
                draft.Revision,
                "correlation"),
            CancellationToken.None);
        OnboardingApplicationConflictException wrongRevision = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => detection.DetectAsync(
                new DetectDuplicateCandidatesCommand(_institution, _reviewer, submitted.ApplicationId, submitted.Revision - 1, "correlation"),
                CancellationToken.None));
        Assert.Equal("onboarding_revision_conflict", wrongRevision.Code);
    }

    [Fact]
    public async Task Another_institution_cannot_read_or_decide_the_set()
    {
        await SubmitAsync(
            "DUP-10-A",
            Capture("Ada", "Lovelace", new DateOnly(1990, 5, 12), "tenant@example.com", "12 Analytical Engine Way"));
        Submitted second = await SubmitAsync(
            "DUP-10-B",
            Capture("Grace", "Hopper", new DateOnly(1906, 12, 9), "tenant@example.com", "9 Compiler Road"));

        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        DuplicateCandidateSetView detected = await DetectAsync(context, second);

        await using CustomerOnboardingDbContext otherContext = CreateContext(_otherInstitution);
        DuplicateDetectionService otherService = new(otherContext, _time);

        await Assert.ThrowsAsync<DuplicateCandidateSetNotFoundException>(
            () => otherService.GetAsync(new GetDuplicateCandidateSetQuery(_otherInstitution, _reviewer, detected.SetId), CancellationToken.None));

        await Assert.ThrowsAsync<DuplicateCandidateSetNotFoundException>(
            () => otherService.DecideAsync(
                new DecideDuplicateCandidatesCommand(
                    _otherInstitution,
                    _reviewer,
                    detected.SetId,
                    detected.Version,
                    DuplicateReviewDecision.Distinct,
                    "foreign decision",
                    "correlation"),
                CancellationToken.None));
    }

    private async Task<DuplicateCandidateSetView> DetectAsync(CustomerOnboardingDbContext context, Submitted application)
    {
        DuplicateDetectionService service = new(context, _time);
        return await service.DetectAsync(
            new DetectDuplicateCandidatesCommand(
                _institution,
                _reviewer,
                application.ApplicationId,
                application.Revision,
                "correlation"),
            CancellationToken.None);
    }

    private async Task<DuplicateCandidateSetView> DecideAsync(
        CustomerOnboardingDbContext context,
        string setId,
        long expectedVersion,
        DuplicateReviewDecision decision,
        string reason)
    {
        DuplicateDetectionService service = new(context, _time);
        return await service.DecideAsync(
            new DecideDuplicateCandidatesCommand(
                _institution,
                _reviewer,
                setId,
                expectedVersion,
                decision,
                reason,
                "correlation"),
            CancellationToken.None);
    }

    private async Task<Submitted> SubmitAsync(string reference, OnboardingCapture capture, InstitutionId? institution = null)
    {
        InstitutionId tenant = institution ?? _institution;
        await using CustomerOnboardingDbContext context = CreateContext(tenant);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);
        await EnsureRequirementPackAsync(context, tenant);

        OnboardingApplicationView started = await onboarding.StartAsync(
            new StartIndividualApplicationCommand(tenant, _officer, "idem-start-" + reference, reference, "branch-1", capture, "correlation"),
            CancellationToken.None);

        OnboardingApplicationView submitted = await onboarding.SubmitAsync(
            new SubmitOnboardingApplicationCommand(tenant, _officer, "idem-submit-" + reference, started.ApplicationId, started.Revision, "correlation"),
            CancellationToken.None);

        return new Submitted(submitted.ApplicationId, submitted.Revision, submitted.PartyId!);
    }

    private static OnboardingCapture Capture(string givenName, string familyName, DateOnly birthDate, string email, string line) =>
        new(
            PersonName.Create(givenName, null, familyName),
            BirthDetails.Create(birthDate, "GB", DateOnly.FromDateTime(Start.UtcDateTime)),
            [ContactPoint.Create(ContactPointType.Email, email)],
            [Address.Create(AddressType.Residential, line, null, "London", null, "GB")]);

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

    private async Task EnsureRequirementPackAsync(CustomerOnboardingDbContext context, InstitutionId institution)
    {
        bool effective = await context.RequirementPacks.AnyAsync(pack => pack.Status == "effective");
        if (effective)
        {
            return;
        }

        RequirementService requirements = new(context, new PendingScanDocumentQuarantine(), _time);
        await requirements.PublishPackAsync(
            new PublishRequirementPackCommand(
                institution,
                _officer,
                "phase1-test",
                [OnboardingRequirement.Create("contact_detail", "Contact detail", true, ["email"])],
                "correlation"),
            CancellationToken.None);
    }

    private static ProtectedValueProtectionOptions CreateProtectionOptions() =>
        ProtectedValueProtectionOptions.Create(
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)(32 + index)).ToArray()),
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)(96 + index)).ToArray()));

    private sealed record Submitted(string ApplicationId, int Revision, string PartyId);

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
