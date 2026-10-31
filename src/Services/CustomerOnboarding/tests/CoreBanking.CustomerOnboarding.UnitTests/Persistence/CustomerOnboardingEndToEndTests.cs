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
using CoreBanking.CustomerOnboarding.Application.Privacy;
using CoreBanking.CustomerOnboarding.Application.Requirements;
using CoreBanking.CustomerOnboarding.Contracts;
using CoreBanking.CustomerOnboarding.Domain.Kyc;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;
using CoreBanking.CustomerOnboarding.Domain.Privacy;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using CoreBanking.CustomerOnboarding.Infrastructure.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Integration;
using CoreBanking.CustomerOnboarding.Infrastructure.Kyc;
using CoreBanking.CustomerOnboarding.Infrastructure.Onboarding;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using CoreBanking.CustomerOnboarding.Infrastructure.Privacy;
using CoreBanking.CustomerOnboarding.Infrastructure.Protection;
using CoreBanking.CustomerOnboarding.Infrastructure.Requirements;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Persistence;

/// <summary>
/// End-to-end acceptance evidence for the epic: one staff-led individual onboarding completes capture,
/// requirement gating, a synthetic provider verification with evidence, classification approval, activation
/// and a masked Customer 360 response; a replay creates no duplicate customer; another institution reaches
/// nothing; and no protected value appears in an event or audit payload.
/// Requirements: CUS-032, CUS-033, CUS-034, CUS-028, CUS-021.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CustomerOnboardingEndToEndTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 30, 15, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("e2e-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _officer = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _reviewer = ActorId.From("kyc-reviewer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public CustomerOnboardingEndToEndTests()
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
    public async Task An_individual_is_onboarded_verified_classified_and_activated()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);

        CustomerView customer = await ActivatedCustomerAsync(context, "E2E-1");

        Assert.Equal("active", customer.StatusCode);

        Customer360View view = await new Customer360Service(context, _time).GetAsync(
            new GetCustomer360Query(_institution, _reviewer, customer.CustomerId, ["profile", "kyc"]),
            CancellationToken.None);

        Customer360SectionView profile = view.Sections.Single(section => section.Section == "profile");
        Assert.Equal("available", profile.StatusCode);
        Assert.Equal("active", profile.Fields["status"]);
        Assert.DoesNotContain(profile.Fields.Values, value => value.Contains("ada@example.com", StringComparison.OrdinalIgnoreCase));

        Customer360SectionView kyc = view.Sections.Single(section => section.Section == "kyc");
        Assert.Equal("available", kyc.StatusCode);

        IdentityVerificationRecord verification = await context.IdentityVerifications.SingleAsync();
        Assert.Equal("succeeded", verification.Status);
        Assert.Equal(64, verification.EvidenceHash!.Length);
    }

    [Fact]
    public async Task A_replayed_submission_creates_no_duplicate_customer()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        await EnsureRequirementPackAsync(context);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);

        OnboardingApplicationView started = await onboarding.StartAsync(
            new StartIndividualApplicationCommand(_institution, _officer, "e2e-replay", "E2E-2", "branch-1", CompleteCapture(), "correlation"),
            CancellationToken.None);
        OnboardingApplicationView submitted = await onboarding.SubmitAsync(
            new SubmitOnboardingApplicationCommand(_institution, _officer, "e2e-replay", started.ApplicationId, started.Revision, "correlation"),
            CancellationToken.None);
        OnboardingApplicationView replayed = await onboarding.SubmitAsync(
            new SubmitOnboardingApplicationCommand(_institution, _officer, "e2e-replay", started.ApplicationId, started.Revision, "correlation"),
            CancellationToken.None);

        Assert.Equal(submitted.Revision, replayed.Revision);
        Assert.Equal(1, await context.OnboardingApplications.CountAsync());

        CustomerService customers = new(context, _time);
        CustomerView first = await customers.RegisterAsync(
            new RegisterCustomerCommand(_institution, _officer, submitted.ApplicationId, null, "correlation"),
            CancellationToken.None);

        Assert.Equal(1, await context.Customers.CountAsync());
        Assert.Equal(first.CustomerId, (await context.Customers.SingleAsync()).CustomerId);
    }

    [Fact]
    public async Task Another_institution_reaches_nothing_and_no_protected_value_reaches_an_event()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerView customer = await ActivatedCustomerAsync(context, "E2E-3");

        await using CustomerOnboardingDbContext otherContext = CreateContext(_otherInstitution);
        Assert.Empty(await otherContext.Customers.ToListAsync());
        Assert.Empty(await otherContext.IdentityVerifications.ToListAsync());
        Assert.Empty(await otherContext.OutboxMessages.ToListAsync());

        await Assert.ThrowsAsync<CustomerNotFoundException>(() =>
            new CustomerService(otherContext, _time).GetAsync(
                new GetCustomerQuery(_otherInstitution, _reviewer, customer.CustomerId),
                CancellationToken.None));

        List<string> payloads = await context.OutboxMessages
            .Select(message => message.Envelope)
            .ToListAsync();
        Assert.NotEmpty(payloads);
        Assert.DoesNotContain(payloads, payload => payload.Contains("ada@example.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(payloads, payload => payload.Contains("Analytical Engine Way", StringComparison.OrdinalIgnoreCase));

        List<string> auditDetails = await context.CustomerAuditEvents
            .Select(record => record.Detail)
            .ToListAsync();
        Assert.NotEmpty(auditDetails);
        Assert.DoesNotContain(auditDetails, detail => detail.Contains("ada@example.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(auditDetails, detail => detail.Contains("Analytical Engine Way", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            await context.OutboxMessages.Select(message => message.EventType).ToListAsync(),
            eventType => eventType == CustomerContractV1.CustomerStatusChangedEventType);
        Assert.Contains(
            await context.OutboxMessages.Select(message => message.EventType).ToListAsync(),
            eventType => eventType == CustomerContractV1.AuditEventType);
    }

    private async Task<CustomerView> ActivatedCustomerAsync(CustomerOnboardingDbContext context, string reference)
    {
        await EnsureRequirementPackAsync(context);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);

        OnboardingApplicationView started = await onboarding.StartAsync(
            new StartIndividualApplicationCommand(_institution, _officer, "e2e-start-" + reference, reference, "branch-1", CompleteCapture(), "correlation"),
            CancellationToken.None);
        OnboardingApplicationView submitted = await onboarding.SubmitAsync(
            new SubmitOnboardingApplicationCommand(_institution, _officer, "e2e-submit-" + reference, started.ApplicationId, started.Revision, "correlation"),
            CancellationToken.None);

        CustomerService customers = new(context, _time);
        CustomerView customer = await customers.RegisterAsync(
            new RegisterCustomerCommand(_institution, _officer, submitted.ApplicationId, null, "correlation"),
            CancellationToken.None);

        KycCaseService kyc = new(context, _time);
        KycCaseView kycCase = await kyc.OpenAsync(
            new OpenKycCaseCommand(_institution, _reviewer, submitted.ApplicationId, submitted.Revision, "correlation"),
            CancellationToken.None);

        KycVerificationRunner runner = new(
            context,
            kyc,
            new SyntheticKycProviderAdapter(SyntheticKycProviderScript.Passed),
            _time,
            KycVerificationRunOptions.Default);

        foreach (KycRequirementView requirement in kycCase.Requirements)
        {
            await runner.RunAsync(
                new RunKycVerificationCommand(
                    _institution,
                    _reviewer,
                    kycCase.CaseId,
                    requirement.Code,
                    "identity_document_check",
                    "correlation"),
                CancellationToken.None);
        }

        await customers.ProposeClassificationAsync(
            new ProposeClassificationCommand(_institution, _reviewer, customer.CustomerId, "tier_1", "retail", "low", false, "correlation"),
            CancellationToken.None);
        await customers.ApproveClassificationAsync(
            new ApproveClassificationCommand(_institution, _reviewer, customer.CustomerId, 1, "workflow-classification-1", "correlation"),
            CancellationToken.None);

        return await customers.ActivateAsync(
            new ActivateCustomerCommand(_institution, _reviewer, customer.CustomerId, 1, "correlation"),
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
                "phase1-e2e",
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
