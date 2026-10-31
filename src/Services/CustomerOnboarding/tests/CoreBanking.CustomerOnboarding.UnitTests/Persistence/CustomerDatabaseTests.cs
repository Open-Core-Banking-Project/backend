using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Application.Kyc;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Application.Requirements;
using CoreBanking.CustomerOnboarding.Domain.Customers;
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
/// Customer relationships against a real <c>customer_onboarding_db</c>: institution-owned numbering,
/// activation gated by a completed KYC case and an approved classification, the epic's transition table with
/// approval references, append-only history, published events and tenant isolation.
/// Decision: P1-E03-D09, P1-E03-D10.
/// </summary>
[Trait("Category", "Database")]
public sealed class CustomerDatabaseTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 29, 15, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("cus-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _officer = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _reviewer = ActorId.From("customer-reviewer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public CustomerDatabaseTests()
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
    public async Task A_customer_is_created_from_a_submitted_application_with_an_institution_number()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);

        (string firstApplication, _, string firstParty) = await SubmittedAsync(context, "CUS-1-A");
        (string secondApplication, _, _) = await SubmittedAsync(context, "CUS-1-B");

        CustomerView first = await RegisterAsync(customers, firstApplication);
        CustomerView second = await RegisterAsync(customers, secondApplication);

        Assert.Equal(firstParty, first.PartyId);
        Assert.Equal(CustomerStatus.Pending, first.Status);
        Assert.Equal("1", first.CustomerNumber);
        Assert.Equal("2", second.CustomerNumber);
        Assert.NotEqual(first.CustomerId, second.CustomerId);

        List<OutboxRecord> events = await context.OutboxMessages
            .Where(message => message.EventType == "customer_created")
            .OrderBy(message => message.CreatedAt)
            .ToListAsync();
        Assert.Equal(2, events.Count);

        IntegrationEventEnvelope envelope = EventContract.Deserialize<IntegrationEventEnvelope>(events[0].Envelope)!;
        Assert.Equal("customer-onboarding", envelope.Producer);
        Assert.Equal("corebanking.customer-onboarding.customer.v1", events[0].Topic);
        Assert.Contains("\"customerId\"", envelope.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("ada@example.com", envelope.Payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Registration_requires_a_submitted_application_and_one_customer_per_party()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);
        await EnsureRequirementPackAsync(context);

        OnboardingApplicationView draft = await onboarding.StartAsync(
            new StartIndividualApplicationCommand(_institution, _officer, "idem-start-CUS-2", "CUS-2", "branch-1", CompleteCapture(), "correlation"),
            CancellationToken.None);

        OnboardingApplicationConflictException notSubmitted = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => customers.RegisterAsync(
                new RegisterCustomerCommand(_institution, _officer, draft.ApplicationId, null, "correlation"),
                CancellationToken.None));
        Assert.Equal("customer_application_not_submitted", notSubmitted.Code);

        (string applicationId, _, _) = await SubmittedAsync(context, "CUS-2-B");
        await RegisterAsync(customers, applicationId);

        OnboardingApplicationConflictException duplicate = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => RegisterAsync(customers, applicationId));
        Assert.Equal("customer_exists", duplicate.Code);
    }

    [Fact]
    public async Task Activation_requires_a_completed_kyc_case()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        KycCaseService kyc = new(context, _time);

        (string applicationId, int revision, _) = await SubmittedAsync(context, "CUS-3");
        CustomerView customer = await RegisterAsync(customers, applicationId);

        CustomerView proposed = await customers.ProposeClassificationAsync(
            new ProposeClassificationCommand(_institution, _reviewer, customer.CustomerId, "tier_1", "retail", "low", false, "correlation"),
            CancellationToken.None);
        await customers.ApproveClassificationAsync(
            new ApproveClassificationCommand(_institution, _reviewer, customer.CustomerId, proposed.Classifications[0].Version, "workflow-classification-1", "correlation"),
            CancellationToken.None);

        KycCaseView opened = await kyc.OpenAsync(
            new OpenKycCaseCommand(_institution, _reviewer, applicationId, revision, "correlation"),
            CancellationToken.None);
        Assert.Equal(KycCaseStatus.Open, opened.Status);

        OnboardingApplicationConflictException incomplete = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => customers.ActivateAsync(
                new ActivateCustomerCommand(_institution, _reviewer, customer.CustomerId, 1, "correlation"),
                CancellationToken.None));
        Assert.Equal("kyc_case_incomplete", incomplete.Code);

        foreach (KycRequirementView requirement in opened.Requirements)
        {
            await kyc.WaiveRequirementAsync(
                new WaiveKycRequirementCommand(_institution, _reviewer, opened.CaseId, requirement.Code, "evidence not available in this channel", "correlation"),
                CancellationToken.None);
        }

        CustomerView active = await customers.ActivateAsync(
            new ActivateCustomerCommand(_institution, _reviewer, customer.CustomerId, 1, "correlation"),
            CancellationToken.None);

        Assert.Equal(CustomerStatus.Active, active.Status);
        CustomerStatusChangeView change = Assert.Single(active.StatusHistory);
        Assert.Equal("pending", change.FromCode);
        Assert.Equal("active", change.ToCode);
        Assert.Equal("workflow-classification-1", change.ApprovalReference);

        Assert.True(await context.OutboxMessages.AnyAsync(message => message.EventType == "customer_status_changed"));
    }

    [Fact]
    public async Task Activation_requires_an_approved_classification()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);

        (string applicationId, int revision, _) = await SubmittedAsync(context, "CUS-4");
        CustomerView customer = await RegisterAsync(customers, applicationId);
        await CompleteKycCaseAsync(context, applicationId, revision);

        OnboardingApplicationConflictException noClassification = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => customers.ActivateAsync(
                new ActivateCustomerCommand(_institution, _reviewer, customer.CustomerId, 1, "correlation"),
                CancellationToken.None));
        Assert.Equal("customer_classification_required", noClassification.Code);

        await customers.ProposeClassificationAsync(
            new ProposeClassificationCommand(_institution, _reviewer, customer.CustomerId, "tier_1", "retail", "low", false, "correlation"),
            CancellationToken.None);

        OnboardingApplicationConflictException notApproved = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => customers.ActivateAsync(
                new ActivateCustomerCommand(_institution, _reviewer, customer.CustomerId, 1, "correlation"),
                CancellationToken.None));
        Assert.Equal("customer_classification_required", notApproved.Code);
    }

    [Fact]
    public async Task An_illegal_transition_is_refused_and_records_nothing()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);

        (string applicationId, _, _) = await SubmittedAsync(context, "CUS-5");
        CustomerView customer = await RegisterAsync(customers, applicationId);

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => customers.ChangeStatusAsync(
                new ChangeCustomerStatusCommand(_institution, _reviewer, customer.CustomerId, CustomerStatus.Dormant, "skip activation", null, "correlation"),
                CancellationToken.None));

        Assert.Equal("customer_transition_forbidden", conflict.Code);
        Assert.Empty(await context.CustomerStatusHistory.ToListAsync());
    }

    [Fact]
    public async Task A_sensitive_transition_requires_an_approval_reference()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);

        (string applicationId, int revision, _) = await SubmittedAsync(context, "CUS-6");
        CustomerView customer = await ActivateAsync(customers, context, applicationId, revision);

        OnboardingApplicationConflictException missingApproval = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => customers.ChangeStatusAsync(
                new ChangeCustomerStatusCommand(_institution, _reviewer, customer.CustomerId, CustomerStatus.Deceased, "death notification", null, "correlation"),
                CancellationToken.None));
        Assert.Equal("customer_approval_required", missingApproval.Code);

        CustomerView deceased = await customers.ChangeStatusAsync(
            new ChangeCustomerStatusCommand(_institution, _reviewer, customer.CustomerId, CustomerStatus.Deceased, "death notification", "workflow-death-1", "correlation"),
            CancellationToken.None);

        Assert.Equal(CustomerStatus.Deceased, deceased.Status);
        Assert.Equal("workflow-death-1", deceased.StatusHistory[^1].ApprovalReference);

        OnboardingApplicationConflictException reopen = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => customers.ChangeStatusAsync(
                new ChangeCustomerStatusCommand(_institution, _reviewer, customer.CustomerId, CustomerStatus.Active, "reopen request", "workflow-reopen-1", "correlation"),
                CancellationToken.None));
        Assert.Equal("customer_transition_forbidden", reopen.Code);
    }

    [Fact]
    public async Task Another_institution_cannot_read_the_customer()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);

        (string applicationId, _, _) = await SubmittedAsync(context, "CUS-7");
        CustomerView customer = await RegisterAsync(customers, applicationId);

        await using CustomerOnboardingDbContext otherContext = CreateContext(_otherInstitution);
        CustomerService otherCustomers = new(otherContext, _time);

        await Assert.ThrowsAsync<CustomerNotFoundException>(
            () => otherCustomers.GetAsync(new GetCustomerQuery(_otherInstitution, _reviewer, customer.CustomerId), CancellationToken.None));
    }

    private async Task<CustomerView> ActivateAsync(
        CustomerService customers,
        CustomerOnboardingDbContext context,
        string applicationId,
        int revision)
    {
        CustomerView customer = await RegisterAsync(customers, applicationId);
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

    private async Task<CustomerView> RegisterAsync(CustomerService customers, string applicationId) =>
        await customers.RegisterAsync(
            new RegisterCustomerCommand(_institution, _officer, applicationId, null, "correlation"),
            CancellationToken.None);

    private async Task<(string ApplicationId, int Revision, string PartyId)> SubmittedAsync(
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

        return (submitted.ApplicationId, submitted.Revision, submitted.PartyId!);
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
