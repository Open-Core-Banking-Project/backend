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
/// Customer merges against a real <c>customer_onboarding_db</c>: the source is closed with retained history,
/// an alias is recorded, the merge event carries the plan hash, and completion waits for every required
/// consumer acknowledgement. Decision: P1-E03-D11.
/// </summary>
[Trait("Category", "Database")]
public sealed class CustomerMergeDatabaseTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 29, 19, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("merge-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _officer = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _reviewer = ActorId.From("merge-reviewer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public CustomerMergeDatabaseTests()
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
    public async Task Applying_a_merge_closes_the_source_records_an_alias_and_publishes_the_plan()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        CustomerMergeService merges = new(context, _time);

        CustomerView survivor = await ActiveCustomerAsync(context, customers, "MERGE-1-A");
        CustomerView source = await ActiveCustomerAsync(context, customers, "MERGE-1-B");

        CustomerMergeView proposed = await ProposeAsync(merges, survivor.CustomerId, source.CustomerId);
        Assert.Equal(MergeRequestStatus.Proposed, proposed.Status);
        Assert.Equal(["core-banking", "risk-reporting"], proposed.RequiredConsumers);

        CustomerMergeView applied = await ApplyAsync(merges, context, proposed.MergeRequestId);

        Assert.Equal(MergeRequestStatus.ReconciliationRequired, applied.Status);
        Assert.Equal(["core-banking", "risk-reporting"], applied.OutstandingConsumers);

        CustomerView closedSource = await customers.GetAsync(new GetCustomerQuery(_institution, _reviewer, source.CustomerId), CancellationToken.None);
        Assert.Equal(CustomerStatus.Closed, closedSource.Status);
        CustomerStatusChangeView lastChange = closedSource.StatusHistory[^1];
        Assert.Equal("closed", lastChange.ToCode);
        Assert.Equal("workflow-merge-1", lastChange.ApprovalReference);
        Assert.Contains("merged into", lastChange.Reason, StringComparison.Ordinal);

        CustomerAliasRecord alias = await context.CustomerAliases.SingleAsync();
        Assert.Equal(source.CustomerId, alias.AliasCustomerId);
        Assert.Equal(survivor.CustomerId, alias.SurvivorCustomerId);

        OutboxRecord mergeEvent = await context.OutboxMessages.SingleAsync(message => message.EventType == "customers_merged");
        IntegrationEventEnvelope envelope = EventContract.Deserialize<IntegrationEventEnvelope>(mergeEvent.Envelope)!;
        Assert.Contains(applied.PlanHash, envelope.Payload, StringComparison.Ordinal);
        Assert.Equal(survivor.CustomerId, mergeEvent.BusinessReference);
    }

    [Fact]
    public async Task Completion_waits_for_every_required_consumer_and_acknowledgements_are_idempotent()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        CustomerMergeService merges = new(context, _time);

        CustomerView survivor = await ActiveCustomerAsync(context, customers, "MERGE-2-A");
        CustomerView source = await ActiveCustomerAsync(context, customers, "MERGE-2-B");
        CustomerMergeView proposed = await ProposeAsync(merges, survivor.CustomerId, source.CustomerId);
        CustomerMergeView applied = await ApplyAsync(merges, context, proposed.MergeRequestId);

        CustomerMergeView afterFirst = await AcknowledgeAsync(merges, applied.MergeRequestId, "core-banking");
        Assert.Equal(MergeRequestStatus.ReconciliationRequired, afterFirst.Status);

        CustomerMergeView repeated = await AcknowledgeAsync(merges, applied.MergeRequestId, "core-banking");
        Assert.Single(repeated.Acknowledgements);

        CustomerMergeView completed = await AcknowledgeAsync(merges, applied.MergeRequestId, "risk-reporting");
        Assert.Equal(MergeRequestStatus.Completed, completed.Status);
        Assert.NotNull(completed.CompletedAt);
        Assert.Empty(completed.OutstandingConsumers);
    }

    [Fact]
    public async Task An_unapproved_merge_cannot_be_applied()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        CustomerMergeService merges = new(context, _time);

        CustomerView survivor = await ActiveCustomerAsync(context, customers, "MERGE-3-A");
        CustomerView source = await ActiveCustomerAsync(context, customers, "MERGE-3-B");
        CustomerMergeView proposed = await ProposeAsync(merges, survivor.CustomerId, source.CustomerId);

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => merges.ApplyAsync(
                new ApplyCustomerMergeCommand(_institution, _reviewer, proposed.MergeRequestId, "correlation"),
                CancellationToken.None));

        Assert.Equal("merge_status_invalid", conflict.Code);
        Assert.Empty(await context.CustomerAliases.ToListAsync());
    }

    [Fact]
    public async Task A_source_customer_supports_only_one_active_merge()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        CustomerMergeService merges = new(context, _time);

        CustomerView survivor = await ActiveCustomerAsync(context, customers, "MERGE-4-A");
        CustomerView other = await ActiveCustomerAsync(context, customers, "MERGE-4-B");
        CustomerView source = await ActiveCustomerAsync(context, customers, "MERGE-4-C");

        await ProposeAsync(merges, survivor.CustomerId, source.CustomerId);

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => ProposeAsync(merges, other.CustomerId, source.CustomerId));

        Assert.Equal("merge_source_conflict", conflict.Code);
    }

    [Fact]
    public async Task Merge_requires_both_customers_to_be_mergeable()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        CustomerMergeService merges = new(context, _time);

        CustomerView active = await ActiveCustomerAsync(context, customers, "MERGE-5-A");
        CustomerView pending = await PendingCustomerAsync(context, customers, "MERGE-5-B");

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => ProposeAsync(merges, active.CustomerId, pending.CustomerId));

        Assert.Equal("merge_customer_state_invalid", conflict.Code);
    }

    [Fact]
    public async Task Another_institution_cannot_read_the_merge()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        CustomerMergeService merges = new(context, _time);

        CustomerView survivor = await ActiveCustomerAsync(context, customers, "MERGE-6-A");
        CustomerView source = await ActiveCustomerAsync(context, customers, "MERGE-6-B");
        CustomerMergeView proposed = await ProposeAsync(merges, survivor.CustomerId, source.CustomerId);

        await using CustomerOnboardingDbContext otherContext = CreateContext(_otherInstitution);
        CustomerMergeService other = new(otherContext, _time);

        await Assert.ThrowsAsync<CustomerMergeNotFoundException>(
            () => other.GetAsync(new GetCustomerMergeQuery(_otherInstitution, _reviewer, proposed.MergeRequestId), CancellationToken.None));
    }

    private Task<CustomerMergeView> ProposeAsync(CustomerMergeService merges, string survivorId, string sourceId) =>
        merges.ProposeAsync(
            new ProposeCustomerMergeCommand(
                _institution,
                _reviewer,
                survivorId,
                sourceId,
                "same person confirmed by duplicate review",
                ["core-banking", "risk-reporting"],
                "correlation"),
            CancellationToken.None);

    private async Task<CustomerMergeView> ApplyAsync(
        CustomerMergeService merges,
        CustomerOnboardingDbContext context,
        string mergeRequestId)
    {
        _ = context;
        CustomerMergeView approved = await merges.ApproveAsync(
            new ApproveCustomerMergeCommand(_institution, _reviewer, mergeRequestId, "workflow-merge-1", "correlation"),
            CancellationToken.None);
        Assert.Equal(MergeRequestStatus.Approved, approved.Status);

        return await merges.ApplyAsync(
            new ApplyCustomerMergeCommand(_institution, _reviewer, mergeRequestId, "correlation"),
            CancellationToken.None);
    }

    private Task<CustomerMergeView> AcknowledgeAsync(CustomerMergeService merges, string mergeRequestId, string consumer) =>
        merges.AcknowledgeAsync(
            new AcknowledgeCustomerMergeCommand(_institution, _reviewer, mergeRequestId, consumer, "correlation"),
            CancellationToken.None);

    private async Task<CustomerView> ActiveCustomerAsync(
        CustomerOnboardingDbContext context,
        CustomerService customers,
        string reference)
    {
        (string applicationId, int revision) = await SubmittedAsync(context, reference);
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

    private async Task<CustomerView> PendingCustomerAsync(
        CustomerOnboardingDbContext context,
        CustomerService customers,
        string reference)
    {
        (string applicationId, _) = await SubmittedAsync(context, reference);
        return await RegisterAsync(customers, applicationId);
    }

    private async Task<CustomerView> RegisterAsync(CustomerService customers, string applicationId) =>
        await customers.RegisterAsync(
            new RegisterCustomerCommand(_institution, _officer, applicationId, null, "correlation"),
            CancellationToken.None);

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
