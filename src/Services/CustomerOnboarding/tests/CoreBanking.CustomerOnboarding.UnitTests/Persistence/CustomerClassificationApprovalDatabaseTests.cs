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
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Application.Requirements;
using CoreBanking.CustomerOnboarding.Contracts;
using CoreBanking.CustomerOnboarding.Domain.Customers;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using CoreBanking.CustomerOnboarding.Infrastructure.Approvals;
using CoreBanking.CustomerOnboarding.Infrastructure.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Onboarding;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using CoreBanking.CustomerOnboarding.Infrastructure.Protection;
using CoreBanking.CustomerOnboarding.Infrastructure.Requirements;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Persistence;

/// <summary>
/// The classification approval loop against a real <c>customer_onboarding_db</c>: the exact version is
/// submitted with a frozen content hash, the Workflow decision is applied only to that request and hash,
/// a replay is idempotent, a rejection leaves the version proposed, and a mismatched decision is refused.
/// Decision: P1-E03-D09.
/// </summary>
[Trait("Category", "Database")]
public sealed class CustomerClassificationApprovalDatabaseTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 30, 18, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("appr-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _officer = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _reviewer = ActorId.From("approval-reviewer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _approver = ActorId.From("approval-approver-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public CustomerClassificationApprovalDatabaseTests()
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
    public async Task A_submission_freezes_the_exact_content_hash_and_publishes_a_request()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await ProposedCustomerAsync(context, "APPR-1");
        CustomerService customers = new(context, _time);

        ClassificationApprovalView submission = await customers.SubmitClassificationForApprovalAsync(
            new SubmitClassificationForApprovalCommand(_institution, _reviewer, customerId, 1, "correlation"),
            CancellationToken.None);

        Assert.False(submission.IsReplay);
        Assert.Equal(64, submission.ContentHash.Length);
        Assert.Equal(
            CustomerClassification.ComputeContentHash(customerId, 1, "tier_1", "retail", "low", false),
            submission.ContentHash);

        CustomerClassificationRecord stored = await context.CustomerClassifications.SingleAsync();
        Assert.Equal(submission.ApprovalRequestId, stored.ApprovalRequestId);
        Assert.Equal(submission.ContentHash, stored.ApprovalContentHash);
        Assert.Equal("proposed", stored.Status);

        OutboxRecord request = await context.OutboxMessages.SingleAsync(message =>
            message.EventType == CustomerClassificationApprovalV1.RequestedEventType);
        Assert.Equal(
            EventContract.TopicFor("customer-onboarding", CustomerClassificationApprovalV1.EventFamily, 1),
            request.Topic);
        CustomerApprovalRequestedV1 payload = EventContract.Deserialize<CustomerApprovalRequestedV1>(
            EventContract.Deserialize<IntegrationEventEnvelope>(request.Envelope)!.Payload)!;
        Assert.Equal(customerId, payload.SubjectId);
        Assert.Equal("1", payload.SubjectVersion);
        Assert.Equal(submission.ContentHash, payload.SubjectHash);
        Assert.Equal(CustomerClassificationApprovalV1.RequiredPermission, payload.RequiredPermission);
        Assert.Equal(CustomerClassificationApprovalV1.DefinitionHash, payload.WorkflowDefinitionHash);
    }

    [Fact]
    public async Task Resubmitting_the_same_version_is_idempotent()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await ProposedCustomerAsync(context, "APPR-2");
        CustomerService customers = new(context, _time);

        ClassificationApprovalView first = await customers.SubmitClassificationForApprovalAsync(
            new SubmitClassificationForApprovalCommand(_institution, _reviewer, customerId, 1, "correlation"),
            CancellationToken.None);
        ClassificationApprovalView second = await customers.SubmitClassificationForApprovalAsync(
            new SubmitClassificationForApprovalCommand(_institution, _reviewer, customerId, 1, "correlation"),
            CancellationToken.None);

        Assert.True(second.IsReplay);
        Assert.Equal(first.ApprovalRequestId, second.ApprovalRequestId);
        Assert.Single(await context.OutboxMessages
            .Where(message => message.EventType == CustomerClassificationApprovalV1.RequestedEventType)
            .ToListAsync());
    }

    [Fact]
    public async Task An_approval_decision_approves_the_exact_version()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await ProposedCustomerAsync(context, "APPR-3");
        CustomerService customers = new(context, _time);
        ClassificationApprovalView submission = await customers.SubmitClassificationForApprovalAsync(
            new SubmitClassificationForApprovalCommand(_institution, _reviewer, customerId, 1, "correlation"),
            CancellationToken.None);

        InboxResult result = await ConsumerAsync(context).HandleAsync(
            DecidedEnvelope(submission, "approved"), CancellationToken.None);

        Assert.Equal(InboxResult.Processed, result);
        CustomerClassificationRecord stored = await context.CustomerClassifications.SingleAsync();
        Assert.Equal("approved", stored.Status);
        Assert.NotNull(stored.ApprovedAt);

        CustomerAuditEventRecord audit = await context.CustomerAuditEvents.SingleAsync(record =>
            record.Action == "customer.classification.approved");
        Assert.Equal(customerId, audit.SubjectReference);

        // The owning service acknowledges what it applied, so Workflow never treats approval as application.
        OutboxRecord acknowledgement = await context.OutboxMessages.SingleAsync(message =>
            message.EventType == CustomerApprovalApplicationV1.EventType);
        Assert.Equal("customer-onboarding", acknowledgement.Producer);
        Assert.Equal(submission.ApprovalRequestId, acknowledgement.BusinessReference);
        CustomerApprovalApplicationResultV1 applied = EventContract.Deserialize<CustomerApprovalApplicationResultV1>(
            EventContract.Deserialize<IntegrationEventEnvelope>(acknowledgement.Envelope)!.Payload)!;
        Assert.Equal(CustomerApprovalApplicationV1.Applied, applied.Outcome);
        Assert.Equal(submission.ApprovalRequestId, applied.RequestId);
        Assert.Equal(submission.ContentHash, applied.SubjectHash);
        Assert.Equal(CustomerClassificationApprovalV1.WorkflowKey, applied.WorkflowKey);
    }

    [Fact]
    public async Task A_repeated_decision_changes_nothing()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await ProposedCustomerAsync(context, "APPR-4");
        CustomerService customers = new(context, _time);
        ClassificationApprovalView submission = await customers.SubmitClassificationForApprovalAsync(
            new SubmitClassificationForApprovalCommand(_institution, _reviewer, customerId, 1, "correlation"),
            CancellationToken.None);

        IntegrationEventEnvelope envelope = DecidedEnvelope(submission, "approved");
        await ConsumerAsync(context).HandleAsync(envelope, CancellationToken.None);
        InboxResult replay = await ConsumerAsync(context).HandleAsync(envelope, CancellationToken.None);

        Assert.Equal(InboxResult.Duplicate, replay);
        Assert.Single(await context.CustomerAuditEvents
            .Where(record => record.Action == "customer.classification.approved")
            .ToListAsync());
    }

    [Fact]
    public async Task A_rejection_leaves_the_version_proposed_and_records_evidence()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await ProposedCustomerAsync(context, "APPR-5");
        CustomerService customers = new(context, _time);
        ClassificationApprovalView submission = await customers.SubmitClassificationForApprovalAsync(
            new SubmitClassificationForApprovalCommand(_institution, _reviewer, customerId, 1, "correlation"),
            CancellationToken.None);

        await ConsumerAsync(context).HandleAsync(DecidedEnvelope(submission, "rejected", reason: "risk evidence missing"), CancellationToken.None);

        CustomerClassificationRecord stored = await context.CustomerClassifications.SingleAsync();
        Assert.Equal("proposed", stored.Status);
        CustomerAuditEventRecord audit = await context.CustomerAuditEvents.SingleAsync(record =>
            record.Action == "customer.classification.rejected");
        Assert.Contains("risk evidence missing", audit.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_decision_with_a_different_hash_is_refused()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await ProposedCustomerAsync(context, "APPR-6");
        CustomerService customers = new(context, _time);
        ClassificationApprovalView submission = await customers.SubmitClassificationForApprovalAsync(
            new SubmitClassificationForApprovalCommand(_institution, _reviewer, customerId, 1, "correlation"),
            CancellationToken.None);

        InboxResult result = await ConsumerAsync(context).HandleAsync(
            DecidedEnvelope(submission, "approved", hash: new string('b', 64)),
            CancellationToken.None);

        Assert.Equal(InboxResult.Deferred, result);
        CustomerClassificationRecord stored = await context.CustomerClassifications.SingleAsync();
        Assert.Equal("proposed", stored.Status);
        Assert.Empty(await context.CustomerAuditEvents
            .Where(record => record.Action == "customer.classification.approved")
            .ToListAsync());
    }

    [Fact]
    public async Task A_decision_for_an_unsubmitted_version_is_refused()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await ProposedCustomerAsync(context, "APPR-7");
        CustomerService customers = new(context, _time);
        ClassificationApprovalView submission = await customers.SubmitClassificationForApprovalAsync(
            new SubmitClassificationForApprovalCommand(_institution, _reviewer, customerId, 1, "correlation"),
            CancellationToken.None);

        InboxResult result = await ConsumerAsync(context).HandleAsync(
            DecidedEnvelope(submission, "approved", requestId: Guid.NewGuid().ToString("N")),
            CancellationToken.None);

        Assert.Equal(InboxResult.Deferred, result);
        CustomerClassificationRecord stored = await context.CustomerClassifications.SingleAsync();
        Assert.Equal("proposed", stored.Status);
    }

    private CustomerClassificationApprovalDecidedConsumer ConsumerAsync(CustomerOnboardingDbContext context) =>
        new(context, new InboxProcessor(context, _time), new AmbientTenantContext(), _time);

    private IntegrationEventEnvelope DecidedEnvelope(
        ClassificationApprovalView submission,
        string decision,
        string? hash = null,
        string? requestId = null,
        string? reason = null)
    {
        string decisionId = Guid.NewGuid().ToString("N");
        string effectiveRequestId = requestId ?? submission.ApprovalRequestId;
        return new IntegrationEventEnvelope
        {
            EventId = Guid.NewGuid().ToString("N"),
            EventType = CustomerClassificationApprovalV1.DecidedEventType,
            SchemaVersion = 1,
            OccurredAt = Start,
            InstitutionId = _institution.Value,
            CorrelationId = "correlation",
            Producer = "operations-workflow",
            BusinessReference = effectiveRequestId,
            Payload = EventContract.Serialize(new CustomerApprovalDecidedV1(
                _institution.Value,
                effectiveRequestId,
                decisionId,
                decision,
                CustomerClassificationApprovalV1.WorkflowKey,
                CustomerClassificationApprovalV1.WorkflowVersionId,
                CustomerClassificationApprovalV1.DefinitionHash,
                CustomerClassificationApprovalV1.SubjectType,
                submission.CustomerId,
                submission.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hash ?? submission.ContentHash,
                _approver.Value,
                CustomerClassificationApprovalV1.RequiredPermission,
                "entitlement-v1",
                Start.AddHours(-1),
                Start.AddHours(1),
                reason,
                "correlation",
                Start)),
        };
    }

    private async Task<string> ProposedCustomerAsync(CustomerOnboardingDbContext context, string reference)
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

        await customers.ProposeClassificationAsync(
            new ProposeClassificationCommand(_institution, _reviewer, customer.CustomerId, "tier_1", "retail", "low", false, "correlation"),
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
