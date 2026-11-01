using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Application.Privacy;
using CoreBanking.CustomerOnboarding.Application.Requirements;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;
using CoreBanking.CustomerOnboarding.Domain.Privacy;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using CoreBanking.CustomerOnboarding.Infrastructure.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Onboarding;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using CoreBanking.CustomerOnboarding.Infrastructure.Privacy;
using CoreBanking.CustomerOnboarding.Infrastructure.Protection;
using CoreBanking.CustomerOnboarding.Infrastructure.Requirements;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Persistence;

/// <summary>
/// Preferences and consent evidence against a real <c>customer_onboarding_db</c>: versioned decisions, an
/// idempotent repeat, a lawful basis for grants, a recorded withdrawal reason, append-only evidence and one
/// current version per purpose and channel. Decision: P1-E03-D12.
/// </summary>
[Trait("Category", "Database")]
public sealed class CustomerPreferencesDatabaseTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 29, 17, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("pref-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _officer = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _customerActor = ActorId.From("customer-actor-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public CustomerPreferencesDatabaseTests()
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
    public async Task A_first_preference_records_version_one_and_consent_evidence()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await CustomerAsync(context, "PREF-1");
        CustomerPreferencesService preferences = new(context, _time);

        CustomerPreferencesView view = await SetAsync(preferences, customerId, PreferenceState.Permitted, "consent", reason: null);

        ServicePreferenceView preference = Assert.Single(view.Preferences);
        Assert.Equal(1, preference.Version);
        Assert.Equal("permitted", preference.StateCode);
        Assert.Equal("consent", preference.LawfulBasis);
        Assert.True(preference.IsCurrent);
        Assert.Equal("customer", preference.Source);

        ConsentEvidenceView evidence = Assert.Single(view.ConsentHistory);
        Assert.Equal("permitted", evidence.StateCode);
        Assert.Null(evidence.WithdrawnAt);

        ServicePreferenceRecord stored = await context.ServicePreferences.SingleAsync();
        Assert.Equal("marketing", stored.Purpose);
        Assert.Equal("mobile_app", stored.Channel);
    }

    [Fact]
    public async Task Repeating_the_same_decision_creates_no_new_version()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await CustomerAsync(context, "PREF-2");
        CustomerPreferencesService preferences = new(context, _time);

        await SetAsync(preferences, customerId, PreferenceState.Denied, lawfulBasis: null, reason: null);
        CustomerPreferencesView second = await SetAsync(preferences, customerId, PreferenceState.Denied, lawfulBasis: null, reason: null);

        Assert.Single(second.Preferences);
        Assert.Single(second.ConsentHistory);
        Assert.Equal(1, await context.ServicePreferences.CountAsync());
        Assert.Equal(1, await context.ConsentEvidence.CountAsync());
    }

    [Fact]
    public async Task A_change_closes_the_current_version_and_appends_the_next()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await CustomerAsync(context, "PREF-3");
        CustomerPreferencesService preferences = new(context, _time);

        await SetAsync(preferences, customerId, PreferenceState.Permitted, "consent", reason: null);
        _time.Advance(TimeSpan.FromMinutes(5));
        CustomerPreferencesView changed = await SetAsync(
            preferences,
            customerId,
            PreferenceState.Denied,
            lawfulBasis: null,
            reason: "customer withdrew marketing consent by phone");

        Assert.Equal(2, changed.Preferences.Count);
        ServicePreferenceView earlier = changed.Preferences[0];
        ServicePreferenceView current = changed.Preferences[1];
        Assert.Equal(1, earlier.Version);
        Assert.False(earlier.IsCurrent);
        Assert.Equal(Start.AddMinutes(5), earlier.EffectiveTo);
        Assert.Equal(2, current.Version);
        Assert.True(current.IsCurrent);
        Assert.Equal("customer withdrew marketing consent by phone", current.Reason);

        List<ConsentEvidenceRecord> evidence = await context.ConsentEvidence
            .OrderBy(record => record.Version)
            .ToListAsync();
        Assert.Equal(2, evidence.Count);
        Assert.Null(evidence[0].WithdrawnAt);
        Assert.Equal(Start.AddMinutes(5), evidence[1].WithdrawnAt);
    }

    [Fact]
    public async Task Withdrawing_a_grant_requires_a_reason()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await CustomerAsync(context, "PREF-4");
        CustomerPreferencesService preferences = new(context, _time);

        await SetAsync(preferences, customerId, PreferenceState.Permitted, "consent", reason: null);

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => SetAsync(preferences, customerId, PreferenceState.Denied, lawfulBasis: null, reason: "  "));
        Assert.Equal("preference_reason_required", conflict.Code);

        Assert.Equal(1, await context.ServicePreferences.CountAsync());
        Assert.Equal(1, await context.ConsentEvidence.CountAsync());
    }

    [Fact]
    public async Task A_grant_without_a_lawful_basis_is_refused()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await CustomerAsync(context, "PREF-5");
        CustomerPreferencesService preferences = new(context, _time);

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => SetAsync(preferences, customerId, PreferenceState.Permitted, lawfulBasis: null, reason: null));

        Assert.Equal("consent_lawful_basis_required", conflict.Code);
        Assert.Empty(await context.ServicePreferences.ToListAsync());
    }

    [Fact]
    public async Task The_database_allows_only_one_current_version_per_purpose_and_channel()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await CustomerAsync(context, "PREF-6");
        CustomerPreferencesService preferences = new(context, _time);
        await SetAsync(preferences, customerId, PreferenceState.Permitted, "consent", reason: null);

        ServicePreferenceRecord existing = await context.ServicePreferences.SingleAsync();
        context.ServicePreferences.Add(new ServicePreferenceRecord
        {
            InstitutionId = _institution.Value,
            PreferenceId = Guid.NewGuid().ToString("N"),
            CustomerId = customerId,
            Purpose = existing.Purpose,
            Channel = existing.Channel,
            Version = 2,
            State = "denied",
            Source = "officer",
            EffectiveFrom = Start,
            RecordedBy = _officer.Value,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Another_institution_cannot_read_the_preferences()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await CustomerAsync(context, "PREF-7");
        CustomerPreferencesService preferences = new(context, _time);
        await SetAsync(preferences, customerId, PreferenceState.Permitted, "consent", reason: null);

        await using CustomerOnboardingDbContext otherContext = CreateContext(_otherInstitution);
        CustomerPreferencesService other = new(otherContext, _time);

        await Assert.ThrowsAsync<CustomerNotFoundException>(
            () => other.GetAsync(new GetCustomerPreferencesQuery(_otherInstitution, _customerActor, customerId), CancellationToken.None));
    }

    private Task<CustomerPreferencesView> SetAsync(
        CustomerPreferencesService preferences,
        string customerId,
        PreferenceState state,
        string? lawfulBasis,
        string? reason) =>
        preferences.SetAsync(
            new SetCustomerPreferenceCommand(
                _institution,
                _customerActor,
                customerId,
                "marketing",
                "mobile_app",
                state,
                lawfulBasis,
                "customer",
                null,
                reason,
                "correlation"),
            CancellationToken.None);

    private async Task<string> CustomerAsync(CustomerOnboardingDbContext context, string reference)
    {
        await EnsureRequirementPackAsync(context);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);

        OnboardingApplicationView started = await onboarding.StartAsync(
            new StartIndividualApplicationCommand(_institution, _officer, "idem-start-" + reference, reference, "branch-1", CompleteCapture(), "correlation"),
            CancellationToken.None);

        OnboardingApplicationView submitted = await onboarding.SubmitAsync(
            new SubmitOnboardingApplicationCommand(_institution, _officer, "idem-submit-" + reference, started.ApplicationId, started.Revision, "correlation"),
            CancellationToken.None);

        CustomerService customers = new(context, _time);
        CustomerView customer = await customers.RegisterAsync(
            new RegisterCustomerCommand(_institution, _officer, submitted.ApplicationId, null, "correlation"),
            CancellationToken.None);

        return customer.CustomerId;
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
                [OnboardingRequirement.Create("contact_detail", "Contact detail", true, ["document"])],
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
