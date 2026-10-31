using System;
using System.Collections.Generic;
using System.Globalization;
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
using CoreBanking.CustomerOnboarding.Contracts;
using CoreBanking.CustomerOnboarding.Domain.Customers;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using CoreBanking.CustomerOnboarding.Infrastructure.Approvals;
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
/// The merge approval loop against a real <c>customer_onboarding_db</c>: the frozen plan hash is submitted as
/// the approval subject, the Workflow decision applies only to that merge request and plan, a rejection ends
/// the request with its reason, a different hash is refused and a repeat changes nothing.
/// Decision: P1-E03-D11.
/// </summary>
[Trait("Category", "Database")]
public sealed class CustomerMergeApprovalDatabaseTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 30, 19, 30, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("mappr-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _officer = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _reviewer = ActorId.From("merge-reviewer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _approver = ActorId.From("merge-approver-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public CustomerMergeApprovalDatabaseTests()
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
    public async Task A_submission_freezes_the_plan_hash_and_publishes_a_request()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        (CustomerMergeService merges, string mergeRequestId, string planHash) = await ProposedMergeAsync(context, "MAPP-1");

        CustomerMergeView submitted = await merges.SubmitForApprovalAsync(
            new SubmitCustomerMergeForApprovalCommand(_institution, _reviewer, mergeRequestId, "correlation"),
            CancellationToken.None);

        Assert.Equal(MergeRequestStatus.PendingApproval, submitted.Status);

        OutboxRecord request = await context.OutboxMessages.SingleAsync(message =>
            message.EventType == CustomerClassificationApprovalV1.RequestedEventType);
        CustomerApprovalRequestedV1 payload = EventContract.Deserialize<CustomerApprovalRequestedV1>(
            EventContract.Deserialize<IntegrationEventEnvelope>(request.Envelope)!.Payload)!;
        Assert.Equal(mergeRequestId, payload.RequestId);
        Assert.Equal(CustomerMergeApprovalV1.SubjectType, payload.SubjectType);
        Assert.Equal(planHash, payload.SubjectHash);
        Assert.Equal(CustomerMergeApprovalV1.RequiredPermission, payload.RequiredPermission);

        CustomerAuditEventRecord audit = await context.CustomerAuditEvents.SingleAsync(record =>
            record.Action == "customer.merge.submitted");
        Assert.Equal(mergeRequestId, audit.SubjectReference);
    }

    [Fact]
    public async Task Resubmitting_a_pending_plan_publishes_nothing_new()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        (CustomerMergeService merges, string mergeRequestId, _) = await ProposedMergeAsync(context, "MAPP-2");

        await merges.SubmitForApprovalAsync(
            new SubmitCustomerMergeForApprovalCommand(_institution, _reviewer, mergeRequestId, "correlation"),
            CancellationToken.None);
        await merges.SubmitForApprovalAsync(
            new SubmitCustomerMergeForApprovalCommand(_institution, _reviewer, mergeRequestId, "correlation"),
            CancellationToken.None);

        Assert.Single(await context.OutboxMessages
            .Where(message => message.EventType == CustomerClassificationApprovalV1.RequestedEventType)
            .ToListAsync());
        Assert.Equal("pending_approval", (await context.MergeRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task An_approval_decision_makes_the_frozen_plan_applicable()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        (CustomerMergeService merges, string mergeRequestId, string planHash) = await ProposedMergeAsync(context, "MAPP-3");

        await merges.SubmitForApprovalAsync(
            new SubmitCustomerMergeForApprovalCommand(_institution, _reviewer, mergeRequestId, "correlation"),
            CancellationToken.None);

        InboxResult result = await ConsumerAsync(context).HandleAsync(
            DecidedEnvelope(mergeRequestId, planHash, "approved"),
            CancellationToken.None);

        Assert.Equal(InboxResult.Processed, result);
        MergeRequestRecord stored = await context.MergeRequests.SingleAsync();
        Assert.Equal("approved", stored.Status);
        Assert.NotNull(stored.ApprovalReference);

        Assert.Single(await context.CustomerAuditEvents
            .Where(record => record.Action == "customer.merge.approved")
            .ToListAsync());
    }

    [Fact]
    public async Task A_repeated_decision_changes_nothing()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        (CustomerMergeService merges, string mergeRequestId, string planHash) = await ProposedMergeAsync(context, "MAPP-4");

        await merges.SubmitForApprovalAsync(
            new SubmitCustomerMergeForApprovalCommand(_institution, _reviewer, mergeRequestId, "correlation"),
            CancellationToken.None);

        IntegrationEventEnvelope envelope = DecidedEnvelope(mergeRequestId, planHash, "approved");
        await ConsumerAsync(context).HandleAsync(envelope, CancellationToken.None);
        InboxResult replay = await ConsumerAsync(context).HandleAsync(envelope, CancellationToken.None);

        Assert.Equal(InboxResult.Duplicate, replay);
        Assert.Single(await context.CustomerAuditEvents
            .Where(record => record.Action == "customer.merge.approved")
            .ToListAsync());
    }

    [Fact]
    public async Task A_rejection_ends_the_request_with_its_reason()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        (CustomerMergeService merges, string mergeRequestId, string planHash) = await ProposedMergeAsync(context, "MAPP-5");

        await merges.SubmitForApprovalAsync(
            new SubmitCustomerMergeForApprovalCommand(_institution, _reviewer, mergeRequestId, "correlation"),
            CancellationToken.None);

        await ConsumerAsync(context).HandleAsync(
            DecidedEnvelope(mergeRequestId, planHash, "rejected", reason: "survivor evidence incomplete"),
            CancellationToken.None);

        MergeRequestRecord stored = await context.MergeRequests.SingleAsync();
        Assert.Equal("rejected", stored.Status);
        // The rejection reason is evidence: it lives in the audit row, not on the merge plan.
        Assert.Null(stored.ApprovalReference);

        CustomerAuditEventRecord audit = await context.CustomerAuditEvents.SingleAsync(record =>
            record.Action == "customer.merge.rejected");
        Assert.Contains("survivor evidence incomplete", audit.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_decision_with_a_different_plan_hash_is_refused()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        (CustomerMergeService merges, string mergeRequestId, _) = await ProposedMergeAsync(context, "MAPP-6");

        await merges.SubmitForApprovalAsync(
            new SubmitCustomerMergeForApprovalCommand(_institution, _reviewer, mergeRequestId, "correlation"),
            CancellationToken.None);

        InboxResult result = await ConsumerAsync(context).HandleAsync(
            DecidedEnvelope(mergeRequestId, new string('b', 64), "approved"),
            CancellationToken.None);

        Assert.Equal(InboxResult.Deferred, result);
        Assert.Equal("pending_approval", (await context.MergeRequests.SingleAsync()).Status);
    }

    private CustomerMergeApprovalDecidedConsumer ConsumerAsync(CustomerOnboardingDbContext context) =>
        new(context, new InboxProcessor(context, _time), new AmbientTenantContext(), _time);

    private IntegrationEventEnvelope DecidedEnvelope(string mergeRequestId, string planHash, string decision, string? reason = null) =>
        new()
        {
            EventId = Guid.NewGuid().ToString("N"),
            EventType = CustomerClassificationApprovalV1.DecidedEventType,
            SchemaVersion = 1,
            OccurredAt = Start,
            InstitutionId = _institution.Value,
            CorrelationId = "correlation",
            Producer = "operations-workflow",
            BusinessReference = mergeRequestId,
            Payload = EventContract.Serialize(new CustomerApprovalDecidedV1(
                _institution.Value,
                mergeRequestId,
                Guid.NewGuid().ToString("N"),
                decision,
                CustomerMergeApprovalV1.WorkflowKey,
                CustomerMergeApprovalV1.WorkflowVersionId,
                CustomerMergeApprovalV1.DefinitionHash,
                CustomerMergeApprovalV1.SubjectType,
                mergeRequestId,
                "1",
                planHash,
                _approver.Value,
                CustomerMergeApprovalV1.RequiredPermission,
                "entitlement-v1",
                Start.AddHours(-1),
                Start.AddHours(1),
                reason,
                "correlation",
                Start)),
        };

    private async Task<(CustomerMergeService Merges, string MergeRequestId, string PlanHash)> ProposedMergeAsync(
        CustomerOnboardingDbContext context,
        string reference)
    {
        CustomerService customers = new(context, _time);
        CustomerView survivor = await ActiveCustomerAsync(context, customers, reference + "-A");
        CustomerView source = await ActiveCustomerAsync(context, customers, reference + "-B");

        CustomerMergeService merges = new(context, _time);
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

        MergeRequestRecord record = await context.MergeRequests
            .SingleAsync(request => request.MergeRequestId == proposed.MergeRequestId);

        return (merges, proposed.MergeRequestId, record.PlanHash);
    }

    private async Task<CustomerView> ActiveCustomerAsync(
        CustomerOnboardingDbContext context,
        CustomerService customers,
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

        CustomerView customer = await customers.RegisterAsync(
            new RegisterCustomerCommand(_institution, _officer, submitted.ApplicationId, null, "correlation"),
            CancellationToken.None);

        await customers.ProposeClassificationAsync(
            new ProposeClassificationCommand(_institution, _reviewer, customer.CustomerId, "tier_1", "retail", "low", false, "correlation"),
            CancellationToken.None);
        await customers.ApproveClassificationAsync(
            new ApproveClassificationCommand(_institution, _reviewer, customer.CustomerId, 1, "workflow-classification-1", "correlation"),
            CancellationToken.None);

        KycCaseService kyc = new(context, _time);
        KycCaseView kycCase = await kyc.OpenAsync(
            new OpenKycCaseCommand(_institution, _reviewer, submitted.ApplicationId, submitted.Revision, "correlation"),
            CancellationToken.None);

        foreach (KycRequirementView requirement in kycCase.Requirements)
        {
            await kyc.WaiveRequirementAsync(
                new WaiveKycRequirementCommand(_institution, _reviewer, kycCase.CaseId, requirement.Code, "evidence not available in this channel", "correlation"),
                CancellationToken.None);
        }

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
