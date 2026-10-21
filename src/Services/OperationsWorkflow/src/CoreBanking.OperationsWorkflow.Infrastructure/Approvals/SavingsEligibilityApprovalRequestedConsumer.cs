using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.OperationsWorkflow.Contracts;
using CoreBanking.OperationsWorkflow.Domain.Approvals;
using CoreBanking.OperationsWorkflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.OperationsWorkflow.Infrastructure.Approvals;

public sealed class SavingsEligibilityApprovalRequestedConsumer : IIntegrationEventConsumer
{
    public const string SourceProducer = "core-ledger";
    public const string EventFamily = "approval";
    public const string EventType = "approval_requested";
    public const string ConsumerName = "operations-workflow.savings-eligibility-approval-requested";
    public const int SchemaVersion = 1;

    private readonly OperationsWorkflowDbContext _context;
    private readonly InboxProcessor _inbox;
    private readonly AmbientTenantContext _tenantContext;

    public SavingsEligibilityApprovalRequestedConsumer(
        OperationsWorkflowDbContext context,
        InboxProcessor inbox,
        AmbientTenantContext tenantContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public static string TopicName => EventContract.TopicFor(SourceProducer, EventFamily, SchemaVersion);
    public string Topic => TopicName;

    public async Task<InboxResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _tenantContext.Clear();
        _tenantContext.Establish(InstitutionId.From(envelope.InstitutionId));
        try
        {
            return await _inbox.ProcessAsync(
                ConsumerName, envelope, SchemaVersion, token => IngestAsync(envelope, token), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _tenantContext.Clear();
        }
    }

    private async Task<string?> IngestAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!string.Equals(envelope.Producer, SourceProducer, StringComparison.Ordinal)
            || !string.Equals(envelope.EventType, EventType, StringComparison.Ordinal))
            throw new InvalidOperationException("The approval topic contained an unexpected producer or event type.");

        ApprovalRequestedV1 payload = EventContract.Deserialize<ApprovalRequestedV1>(envelope.Payload)
            ?? throw new InvalidOperationException("The approval request payload is missing.");
        Validate(envelope, payload);
        InstitutionBoundaryRecord boundary = await _context.InstitutionBoundaries.SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The institution boundary must exist before approval ingestion.");
        if (!string.Equals(boundary.Status, "active", StringComparison.Ordinal))
            throw new InvalidOperationException("The institution boundary must be active before approval ingestion.");

        ApprovalRequestRecord? existing = await _context.ApprovalRequests
            .SingleOrDefaultAsync(x => x.SourceService == payload.SourceService
                && x.SourceRequestKey == payload.SourceRequestKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestId, payload.RequestId, StringComparison.Ordinal)
                || !string.Equals(existing.SubjectVersion, payload.SubjectVersion, StringComparison.Ordinal)
                || !string.Equals(existing.SubjectHash, payload.SubjectHash, StringComparison.Ordinal))
                throw new InvalidOperationException("The source request key is already bound to a different approval subject.");
            return "already_submitted:" + existing.RequestId;
        }

        ApprovalRequest aggregate = ApprovalRequest.SubmitSavingsEligibilityPublication(
            InstitutionId.From(payload.InstitutionId), payload.RequestId, payload.SourceRequestKey,
            payload.SubjectId, payload.SubjectVersion, payload.SubjectHash,
            ActorId.From(payload.MakerActorId), payload.CorrelationId, payload.SubmittedAt);
        _context.ApprovalRequests.Add(new ApprovalRequestRecord
        {
            InstitutionId = aggregate.InstitutionId.Value,
            RequestId = aggregate.RequestId,
            SourceRequestKey = aggregate.SourceRequestKey,
            WorkflowKey = aggregate.WorkflowKey,
            WorkflowVersionId = aggregate.WorkflowVersionId,
            WorkflowDefinitionHash = aggregate.WorkflowDefinitionHash,
            SourceService = aggregate.SourceService,
            SubjectType = aggregate.SubjectType,
            SubjectId = aggregate.SubjectId,
            SubjectVersion = aggregate.SubjectVersion,
            SubjectHash = aggregate.SubjectHash,
            MakerActorId = aggregate.Maker.Value,
            RequiredPermission = aggregate.RequiredPermission,
            CorrelationId = aggregate.CorrelationId,
            Status = "submitted",
            Version = 1,
            SubmittedAt = aggregate.SubmittedAt,
        });
        return "submitted:" + aggregate.RequestId;
    }

    private static void Validate(IntegrationEventEnvelope envelope, ApprovalRequestedV1 payload)
    {
        if (!string.Equals(payload.InstitutionId, envelope.InstitutionId, StringComparison.Ordinal)
            || !string.Equals(payload.RequestId, envelope.BusinessReference, StringComparison.Ordinal)
            || !string.Equals(payload.CorrelationId, envelope.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowKey, SavingsEligibilityPublicationProfile.WorkflowKey, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowVersionId, SavingsEligibilityPublicationProfile.WorkflowVersionId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowDefinitionHash, SavingsEligibilityPublicationProfile.DefinitionHash, StringComparison.Ordinal)
            || !string.Equals(payload.SourceService, SavingsEligibilityPublicationProfile.SourceService, StringComparison.Ordinal)
            || !string.Equals(payload.SubjectType, SavingsEligibilityPublicationProfile.SubjectType, StringComparison.Ordinal)
            || !string.Equals(payload.RequiredPermission, SavingsEligibilityPublicationProfile.RequiredPermission, StringComparison.Ordinal))
            throw new InvalidOperationException("The approval request does not match the approved workflow profile or trusted envelope.");
    }
}
