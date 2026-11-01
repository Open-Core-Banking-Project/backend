using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>
/// Ingests <c>approval_requested</c> from every approved source service. The profile catalogue decides whether a
/// request is acceptable; the source service, subject type, version, definition hash and permission all have to
/// match the approved profile, so one consumer serves every maker-checker flow without trusting the caller.
/// Decision: ADR-0016 and epic 06 slices 4, 5 and 8.
/// </summary>
public sealed class ApprovalRequestedConsumer : IIntegrationEventConsumer
{
    public const string EventFamily = "approval";
    public const string EventType = "approval_requested";
    public const string ConsumerName = "operations-workflow.approval-requested";
    public const int SchemaVersion = 1;

    private readonly OperationsWorkflowDbContext _context;
    private readonly InboxProcessor _inbox;
    private readonly AmbientTenantContext _tenantContext;

    public ApprovalRequestedConsumer(
        OperationsWorkflowDbContext context,
        InboxProcessor inbox,
        AmbientTenantContext tenantContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <summary>Every topic an approved source service publishes its requests on.</summary>
    public static IReadOnlyList<string> TopicNames { get; } =
        [.. ApprovalProfiles.SourceServices.Select(source => EventContract.TopicFor(source, EventFamily, SchemaVersion))];

    public string Topic => throw new NotSupportedException("The approval consumer subscribes to every approved source topic.");

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
        if (!string.Equals(envelope.EventType, EventType, StringComparison.Ordinal))
            throw new InvalidOperationException("The approval topic contained an unexpected event type.");

        ApprovalRequestedV1 payload = EventContract.Deserialize<ApprovalRequestedV1>(envelope.Payload)
            ?? throw new InvalidOperationException("The approval request payload is missing.");
        Validate(envelope, payload);

        ApprovalProfile profile = ApprovalProfiles.Find(payload.WorkflowKey)
            ?? throw new InvalidOperationException("The approval request names an unapproved workflow profile.");

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

        ApprovalRequest aggregate = ApprovalRequest.Submit(
            profile,
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
            || !string.Equals(payload.CorrelationId, envelope.CorrelationId, StringComparison.Ordinal))
            throw new InvalidOperationException("The approval request does not match its trusted envelope.");

        ApprovalProfile profile = ApprovalProfiles.Find(payload.WorkflowKey)
            ?? throw new InvalidOperationException("The approval request names an unapproved workflow profile.");

        // The producing service, subject type, version, definition hash and permission are all pinned by the profile.
        if (!string.Equals(envelope.Producer, profile.SourceService, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowVersionId, profile.WorkflowVersionId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowDefinitionHash, profile.DefinitionHash, StringComparison.Ordinal)
            || !string.Equals(payload.SourceService, profile.SourceService, StringComparison.Ordinal)
            || !string.Equals(payload.SubjectType, profile.SubjectType, StringComparison.Ordinal)
            || !string.Equals(payload.RequiredPermission, profile.RequiredPermission, StringComparison.Ordinal))
            throw new InvalidOperationException("The approval request does not match the approved workflow profile.");
    }
}
