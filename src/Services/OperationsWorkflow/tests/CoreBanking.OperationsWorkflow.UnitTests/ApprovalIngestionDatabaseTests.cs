using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.OperationsWorkflow.Application.Approvals;
using CoreBanking.OperationsWorkflow.Contracts;
using CoreBanking.OperationsWorkflow.Domain.Approvals;
using CoreBanking.OperationsWorkflow.Infrastructure.Approvals;
using CoreBanking.OperationsWorkflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.OperationsWorkflow.UnitTests;

/// <summary>
/// Profile-driven approval ingestion against a real <c>operations_workflow_db</c>: a customer approval request is
/// accepted only when it matches an approved profile, and the decision it produces carries that profile's key,
/// permission and exact subject binding. Decision: ADR-0016, P1-E03-D09, P1-E03-D11.
/// </summary>
[Trait("Category", "Database")]
public sealed class ApprovalIngestionDatabaseTests : IDisposable
{
    private const string ConnectionStringVariableName = "ConnectionStrings__OperationsWorkflow";

    private static readonly DateTimeOffset Start = new(2026, 10, 31, 20, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("approval-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _maker = ActorId.From("approval-maker-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly ActorId _approver = ActorId.From("approval-approver-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly AmbientTenantContext _tenant = new();
    private readonly TestTimeProvider _time = new(Start);

    public ApprovalIngestionDatabaseTests()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringVariableName} to the application principal connection string, or run scripts/db-verify.ps1.");
        }

        _connectionString = configured;
        _tenant.Establish(_institution);
        try
        {
            using OperationsWorkflowDbContext context = CreateContext();
            context.InstitutionBoundaries.Add(new InstitutionBoundaryRecord
            {
                InstitutionId = _institution.Value,
                ProvisioningPlanVersion = 1,
                Status = "active",
                SourceEventId = Guid.NewGuid().ToString("N"),
                InitialisedAt = Start,
            });
            context.SaveChanges();
        }
        finally
        {
            _tenant.Clear();
        }
    }

    public void Dispose()
    {
        _tenant.Establish(_institution);
        try
        {
            using OperationsWorkflowDbContext context = CreateContext();
            context.DeadLetters.ExecuteDelete();
            context.InboxMessages.ExecuteDelete();
            context.OutboxMessages.ExecuteDelete();
            context.ApprovalDecisions.ExecuteDelete();
            context.ApprovalRequests.ExecuteDelete();
            context.InstitutionBoundaries.ExecuteDelete();
        }
        finally
        {
            _tenant.Clear();
        }
    }

    [Fact]
    public async Task A_customer_approval_request_is_ingested_under_its_own_profile_and_decided()
    {
        const string requestId = "approval-request-1";
        const string subjectHash = "1111111111111111111111111111111111111111111111111111111111111111";

        await using OperationsWorkflowDbContext context = CreateContext();
        ApprovalRequestedConsumer consumer = new(context, new InboxProcessor(context, _time), _tenant);

        IntegrationEventEnvelope request = Envelope(requestId, ApprovalProfiles.CustomerClassification, subjectHash);
        InboxResult ingested = await consumer.HandleAsync(request, CancellationToken.None);

        Assert.Equal(InboxResult.Processed, ingested);

        // The consumer establishes and clears the tenant for each message, so the test re-establishes it.
        _tenant.Establish(_institution);

        ApprovalRequestRecord stored = await context.ApprovalRequests.SingleAsync();
        Assert.Equal("customer.classification.approve.v1", stored.WorkflowKey);
        Assert.Equal("customer-onboarding", stored.SourceService);
        Assert.Equal("customer.classification.version", stored.SubjectType);
        Assert.Equal("customer.classification.approve", stored.RequiredPermission);
        Assert.Equal(ApprovalProfiles.CustomerClassification.DefinitionHash, stored.WorkflowDefinitionHash);
        Assert.Equal("submitted", stored.Status);

        // A second delivery of the same event is a duplicate, never a second request.
        Assert.Equal(InboxResult.Duplicate, await consumer.HandleAsync(request, CancellationToken.None));
        _tenant.Establish(_institution);
        Assert.Single(await context.ApprovalRequests.ToListAsync());

        ApprovalWorkflowService workflow = new(
            context,
            new FixedEntitlementVerifier(_approver, ApprovalProfiles.CustomerClassification.RequiredPermission, _time),
            _tenant,
            _time);

        ApprovalDecisionResult decision = await workflow.DecideAsync(
            new DecideApprovalCommand(
                _institution, _approver, requestId, "approval-decision-1", ApprovalDecisionType.Approve, "checked", "correlation-decide"),
            CancellationToken.None);

        Assert.Equal("approved", decision.Decision);
        Assert.Equal(subjectHash, decision.SubjectHash);
        _tenant.Establish(_institution);

        IntegrationEventEnvelope decisionEnvelope = EventContract.Deserialize<IntegrationEventEnvelope>(
            (await context.OutboxMessages.SingleAsync(message => message.EventType == "approval_decided"
                && message.BusinessReference == requestId)).Envelope)
            ?? throw new InvalidOperationException("The decision event envelope is missing.");
        Assert.Equal("operations-workflow", decisionEnvelope.Producer);
        Assert.Equal(requestId, decisionEnvelope.BusinessReference);

        ApprovalDecidedV1 published = EventContract.Deserialize<ApprovalDecidedV1>(decisionEnvelope.Payload)
            ?? throw new InvalidOperationException("The decision event payload is missing.");
        Assert.Equal("customer.classification.approve.v1", published.WorkflowKey);
        Assert.Equal("customer.classification.approve", published.Permission);
        Assert.Equal(ApprovalProfiles.CustomerClassification.DefinitionHash, published.WorkflowDefinitionHash);
        Assert.Equal("customer", published.SubjectId);
    }

    [Fact]
    public async Task A_request_naming_an_unapproved_profile_is_refused_and_never_stored()
    {
        await using OperationsWorkflowDbContext context = CreateContext();
        ApprovalRequestedConsumer consumer = new(context, new InboxProcessor(context, _time), _tenant);

        IntegrationEventEnvelope envelope = Envelope(
            "approval-request-2",
            ApprovalProfiles.CustomerClassification,
            new string('2', 64));
        ApprovalRequestedV1 payload = EventContract.Deserialize<ApprovalRequestedV1>(envelope.Payload)!;
        envelope = envelope with
        {
            Payload = EventContract.Serialize(payload with { WorkflowKey = "customer.classification.approve.v9" }),
        };

        InboxResult refused = await consumer.HandleAsync(envelope, CancellationToken.None);

        // The refusal is deferred for a retry and nothing is stored; the request can never be approved under a
        // profile this service does not know.
        Assert.Equal(InboxResult.Deferred, refused);
        _tenant.Establish(_institution);
        Assert.Empty(await context.ApprovalRequests.ToListAsync());
    }

    [Fact]
    public async Task An_acknowledgement_marks_the_request_applied_and_a_repeat_changes_nothing()
    {
        const string requestId = "approval-request-ack-1";
        const string subjectHash = "3333333333333333333333333333333333333333333333333333333333333333";

        await using OperationsWorkflowDbContext context = CreateContext();
        ApprovalRequestedConsumer requests = new(context, new InboxProcessor(context, _time), _tenant);
        Assert.Equal(InboxResult.Processed, await requests.HandleAsync(
            Envelope(requestId, ApprovalProfiles.CustomerMerge, subjectHash), CancellationToken.None));
        _tenant.Establish(_institution);

        ApprovalWorkflowService workflow = new(
            context,
            new FixedEntitlementVerifier(_approver, ApprovalProfiles.CustomerMerge.RequiredPermission, _time),
            _tenant,
            _time);
        await workflow.DecideAsync(
            new DecideApprovalCommand(
                _institution, _approver, requestId, "decision-ack-1", ApprovalDecisionType.Approve, null, "correlation-decide"),
            CancellationToken.None);
        _tenant.Establish(_institution);

        ApprovalRequestRecord approved = await context.ApprovalRequests.SingleAsync();
        Assert.Null(approved.ApplicationStatus);

        ApprovalApplicationResultConsumer acknowledgements = new(context, new InboxProcessor(context, _time), _tenant);
        IntegrationEventEnvelope applied = Acknowledgement(
            requestId, "decision-ack-1", ApprovalProfiles.CustomerMerge, subjectHash, ApprovalApplicationResultV1Codes.Applied);

        Assert.Equal(InboxResult.Processed, await acknowledgements.HandleAsync(applied, CancellationToken.None));
        _tenant.Establish(_institution);

        ApprovalRequestRecord updated = await context.ApprovalRequests.SingleAsync();
        Assert.Equal("applied", updated.ApplicationStatus);
        Assert.NotNull(updated.AppliedAt);

        ApprovalApplicationResultRecord result = await context.ApprovalApplicationResults.SingleAsync();
        Assert.Equal("applied", result.Outcome);
        Assert.Equal(subjectHash, result.SubjectHash);

        // The same acknowledgement delivered again is a duplicate and changes nothing.
        Assert.Equal(InboxResult.Duplicate, await acknowledgements.HandleAsync(applied, CancellationToken.None));
        _tenant.Establish(_institution);
        Assert.Single(await context.ApprovalApplicationResults.ToListAsync());
    }

    [Fact]
    public async Task An_acknowledgement_that_contradicts_the_record_is_refused()
    {
        const string requestId = "approval-request-ack-2";
        const string subjectHash = "4444444444444444444444444444444444444444444444444444444444444444";

        await using OperationsWorkflowDbContext context = CreateContext();
        ApprovalRequestedConsumer requests = new(context, new InboxProcessor(context, _time), _tenant);
        Assert.Equal(InboxResult.Processed, await requests.HandleAsync(
            Envelope(requestId, ApprovalProfiles.CustomerClassification, subjectHash), CancellationToken.None));
        _tenant.Establish(_institution);

        ApprovalWorkflowService workflow = new(
            context,
            new FixedEntitlementVerifier(_approver, ApprovalProfiles.CustomerClassification.RequiredPermission, _time),
            _tenant,
            _time);
        await workflow.DecideAsync(
            new DecideApprovalCommand(
                _institution, _approver, requestId, "decision-ack-2", ApprovalDecisionType.Reject, "risk evidence missing", "correlation-decide"),
            CancellationToken.None);
        _tenant.Establish(_institution);

        ApprovalApplicationResultConsumer acknowledgements = new(context, new InboxProcessor(context, _time), _tenant);

        // A rejected decision cannot be acknowledged as applied.
        Assert.Equal(InboxResult.Deferred, await acknowledgements.HandleAsync(
            Acknowledgement(requestId, "decision-ack-2", ApprovalProfiles.CustomerClassification, subjectHash, ApprovalApplicationResultV1Codes.Applied),
            CancellationToken.None));
        _tenant.Establish(_institution);

        Assert.Empty(await context.ApprovalApplicationResults.ToListAsync());
        Assert.Null((await context.ApprovalRequests.SingleAsync()).ApplicationStatus);
    }

    private IntegrationEventEnvelope Acknowledgement(
        string requestId,
        string decisionId,
        ApprovalProfile profile,
        string subjectHash,
        string outcome)
    {
        ApprovalApplicationResultV1 payload = new(
            _institution.Value,
            requestId,
            decisionId,
            decisionId,
            profile.WorkflowKey,
            profile.SubjectType,
            "customer",
            "3",
            subjectHash,
            profile.SourceService,
            outcome,
            FailureCode: null,
            Reason: null,
            "correlation-" + requestId,
            Start);

        return new IntegrationEventEnvelope
        {
            EventId = Guid.NewGuid().ToString("N"),
            EventType = ApprovalApplicationResultV1Codes.EventType,
            SchemaVersion = ApprovalApplicationResultV1Codes.SchemaVersion,
            OccurredAt = Start,
            InstitutionId = _institution.Value,
            CorrelationId = payload.CorrelationId,
            Producer = profile.SourceService,
            BusinessReference = requestId,
            Payload = EventContract.Serialize(payload),
        };
    }

    private IntegrationEventEnvelope Envelope(string requestId, ApprovalProfile profile, string subjectHash)
    {
        ApprovalRequestedV1 payload = new(
            _institution.Value,
            requestId,
            "source-" + requestId,
            profile.WorkflowKey,
            profile.WorkflowVersionId,
            profile.DefinitionHash,
            profile.SourceService,
            profile.SubjectType,
            "customer",
            "3",
            subjectHash,
            _maker.Value,
            profile.RequiredPermission,
            "correlation-" + requestId,
            Start);

        return new IntegrationEventEnvelope
        {
            EventId = Guid.NewGuid().ToString("N"),
            EventType = ApprovalRequestedConsumer.EventType,
            SchemaVersion = ApprovalRequestedConsumer.SchemaVersion,
            OccurredAt = Start,
            InstitutionId = _institution.Value,
            CorrelationId = payload.CorrelationId,
            Producer = profile.SourceService,
            BusinessReference = requestId,
            Payload = EventContract.Serialize(payload),
        };
    }

    private OperationsWorkflowDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<OperationsWorkflowDbContext>().UseNpgsql(_connectionString).Options,
            _tenant);

    private sealed class FixedEntitlementVerifier : IApprovalEntitlementVerifier
    {
        private readonly ActorId _approver;
        private readonly string _permission;
        private readonly TimeProvider _timeProvider;

        public FixedEntitlementVerifier(ActorId approver, string permission, TimeProvider timeProvider)
        {
            _approver = approver;
            _permission = permission;
            _timeProvider = timeProvider;
        }

        public Task<EntitlementDecisionEvidence?> VerifyAsync(
            InstitutionId institutionId,
            ActorId actorId,
            string requiredPermission,
            DateTimeOffset decisionTime,
            CancellationToken cancellationToken)
        {
            if (actorId != _approver || !string.Equals(requiredPermission, _permission, StringComparison.Ordinal))
            {
                return Task.FromResult<EntitlementDecisionEvidence?>(null);
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            return Task.FromResult<EntitlementDecisionEvidence?>(EntitlementDecisionEvidence.Create(
                institutionId,
                actorId,
                requiredPermission,
                "6",
                now.AddMinutes(-1),
                now.AddMinutes(4)));
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
