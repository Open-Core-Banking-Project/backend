using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Application.Kyc;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Application.Requirements;
using CoreBanking.CustomerOnboarding.Domain.Kyc;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using CoreBanking.CustomerOnboarding.Infrastructure.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Kyc;
using CoreBanking.CustomerOnboarding.Infrastructure.Onboarding;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using CoreBanking.CustomerOnboarding.Infrastructure.Protection;
using CoreBanking.CustomerOnboarding.Infrastructure.Requirements;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Persistence;

/// <summary>
/// Customer 360 composition against a real <c>customer_onboarding_db</c>: three truthful local sections, explicit
/// unavailable placeholders, masked values only, bounded section requests and tenant isolation.
/// Decision: P1-E03-D13.
/// </summary>
[Trait("Category", "Database")]
public sealed class Customer360DatabaseTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 29, 20, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("c360-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _officer = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _reviewer = ActorId.From("customer-reviewer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public Customer360DatabaseTests()
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
    public async Task The_profile_section_returns_status_classification_and_masked_contacts_only()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        Customer360Service composition = new(context, _time);

        CustomerView customer = await ActiveCustomerAsync(context, customers, "C360-1");

        Customer360View view = await composition.GetAsync(
            new GetCustomer360Query(_institution, _reviewer, customer.CustomerId, ["profile"]),
            CancellationToken.None);

        Customer360SectionView profile = Assert.Single(view.Sections);
        Assert.Equal("profile", profile.Section);
        Assert.Equal(Customer360SectionStatus.Available, profile.Status);
        Assert.Equal("available", profile.StatusCode);
        Assert.Equal("customer-onboarding", profile.Source);
        Assert.Equal("customer.profile.view", profile.RequiredPermission);
        Assert.Equal(Start, profile.AsOf);
        Assert.Null(profile.ReasonCode);

        Assert.Equal(customer.CustomerNumber, profile.Fields["customerNumber"]);
        Assert.Equal("active", profile.Fields["status"]);
        Assert.Equal("tier_1", profile.Fields["tier"]);
        Assert.Equal("false", profile.Fields["enhancedDueDiligence"]);
        Assert.Contains("***", profile.Fields["maskedEmail"], StringComparison.Ordinal);
        Assert.DoesNotContain(
            profile.Fields.Values,
            value => value.Contains("ada@example.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unavailable_sections_are_reported_with_a_reason_and_no_values()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        Customer360Service composition = new(context, _time);

        CustomerView customer = await ActiveCustomerAsync(context, customers, "C360-2");

        Customer360View view = await composition.GetAsync(
            new GetCustomer360Query(_institution, _reviewer, customer.CustomerId, ["accounts", "cases", "risk", "documents", "channels"]),
            CancellationToken.None);

        Assert.Equal(5, view.Sections.Count);
        Assert.All(view.Sections, section =>
        {
            Assert.Equal(Customer360SectionStatus.Unavailable, section.Status);
            Assert.Equal("source_not_integrated", section.ReasonCode);
            Assert.Null(section.AsOf);
            Assert.Empty(section.Fields);
        });
        Assert.Contains(view.Sections, section => section.Section == "accounts" && section.Source == "core-banking");
    }

    [Fact]
    public async Task The_kyc_section_reports_status_and_counts_without_evidence()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        Customer360Service composition = new(context, _time);

        (CustomerView customer, string applicationId, int revision) = await CustomerWithApplicationAsync(context, customers, "C360-3");
        KycCaseService kyc = new(context, _time);
        await kyc.OpenAsync(
            new OpenKycCaseCommand(_institution, _reviewer, applicationId, revision, "correlation"),
            CancellationToken.None);

        Customer360View view = await composition.GetAsync(
            new GetCustomer360Query(_institution, _reviewer, customer.CustomerId, ["kyc"]),
            CancellationToken.None);

        Customer360SectionView section = Assert.Single(view.Sections);
        Assert.Equal("customer.kyc.view", section.RequiredPermission);
        Assert.Equal("open", section.Fields["caseStatus"]);
        Assert.Equal("1", section.Fields["outstandingRequirements"]);
        Assert.Equal("1", section.Fields["requirements_required"]);
        Assert.DoesNotContain(section.Fields.Keys, key => key.Contains("evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_merges_section_reports_open_merges_and_the_alias_redirect()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        CustomerMergeService merges = new(context, _time);
        Customer360Service composition = new(context, _time);

        CustomerView survivor = await ActiveCustomerAsync(context, customers, "C360-4-A");
        CustomerView source = await ActiveCustomerAsync(context, customers, "C360-4-B");

        CustomerMergeView proposed = await merges.ProposeAsync(
            new ProposeCustomerMergeCommand(
                _institution,
                _reviewer,
                survivor.CustomerId,
                source.CustomerId,
                "same person confirmed by duplicate review",
                ["core-banking", "risk-reporting"],
                "correlation"),
            CancellationToken.None);
        await merges.ApproveAsync(
            new ApproveCustomerMergeCommand(_institution, _reviewer, proposed.MergeRequestId, "workflow-merge-1", "correlation"),
            CancellationToken.None);
        await merges.ApplyAsync(
            new ApplyCustomerMergeCommand(_institution, _reviewer, proposed.MergeRequestId, "correlation"),
            CancellationToken.None);

        Customer360View view = await composition.GetAsync(
            new GetCustomer360Query(_institution, _reviewer, source.CustomerId, ["merges"]),
            CancellationToken.None);

        Customer360SectionView section = Assert.Single(view.Sections);
        Assert.Equal("customer.merge.propose", section.RequiredPermission);
        Assert.Equal("1", section.Fields["openMergeRequests"]);
        Assert.Equal(survivor.CustomerId, section.Fields["mergedInto"]);
    }

    [Fact]
    public async Task Section_requests_are_bounded_and_unknown_sections_are_refused()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        Customer360Service composition = new(context, _time);

        CustomerView customer = await ActiveCustomerAsync(context, customers, "C360-5");

        Customer360View empty = await composition.GetAsync(
            new GetCustomer360Query(_institution, _reviewer, customer.CustomerId, []),
            CancellationToken.None);
        Assert.Empty(empty.Sections);

        OnboardingApplicationConflictException unknown = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => composition.GetAsync(
                new GetCustomer360Query(_institution, _reviewer, customer.CustomerId, ["everything"]),
                CancellationToken.None));
        Assert.Equal("customer360_section_unknown", unknown.Code);
    }

    [Fact]
    public async Task Another_institution_cannot_read_the_composition()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        Customer360Service composition = new(context, _time);

        CustomerView customer = await ActiveCustomerAsync(context, customers, "C360-6");

        await using CustomerOnboardingDbContext otherContext = CreateContext(_otherInstitution);
        Customer360Service otherComposition = new(otherContext, _time);

        await Assert.ThrowsAsync<CustomerNotFoundException>(
            () => otherComposition.GetAsync(
                new GetCustomer360Query(_otherInstitution, _reviewer, customer.CustomerId, ["profile"]),
                CancellationToken.None));
    }

    private async Task<CustomerView> ActiveCustomerAsync(
        CustomerOnboardingDbContext context,
        CustomerService customers,
        string reference)
    {
        (CustomerView customer, string applicationId, int revision) = await CustomerWithApplicationAsync(context, customers, reference);

        await customers.ProposeClassificationAsync(
            new ProposeClassificationCommand(_institution, _reviewer, customer.CustomerId, "tier_1", "retail", "low", false, "correlation"),
            CancellationToken.None);
        await customers.ApproveClassificationAsync(
            new ApproveClassificationCommand(_institution, _reviewer, customer.CustomerId, 1, "workflow-classification-1", "correlation"),
            CancellationToken.None);

        await CompleteKycCaseAsync(context, applicationId, revision);

        return await customers.ActivateAsync(
            new ActivateCustomerCommand(_institution, _reviewer, customer.CustomerId, 1, "correlation"),
            CancellationToken.None);
    }

    private async Task<(CustomerView Customer, string ApplicationId, int Revision)> CustomerWithApplicationAsync(
        CustomerOnboardingDbContext context,
        CustomerService customers,
        string reference)
    {
        (string applicationId, int revision) = await SubmittedAsync(context, reference);
        CustomerView customer = await customers.RegisterAsync(
            new RegisterCustomerCommand(_institution, _officer, applicationId, null, "correlation"),
            CancellationToken.None);

        return (customer, applicationId, revision);
    }

    private async Task<KycCaseView> CompleteKycCaseAsync(
        CustomerOnboardingDbContext context,
        string applicationId,
        int revision)
    {
        KycCaseService kyc = new(context, _time);
        KycCaseView opened = await kyc.OpenAsync(
            new OpenKycCaseCommand(_institution, _reviewer, applicationId, revision, "correlation"),
            CancellationToken.None);

        KycCaseView current = opened;
        foreach (KycRequirementView requirement in opened.Requirements)
        {
            current = await kyc.WaiveRequirementAsync(
                new WaiveKycRequirementCommand(_institution, _reviewer, opened.CaseId, requirement.Code, "evidence not available in this channel", "correlation"),
                CancellationToken.None);
        }

        return current;
    }

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
