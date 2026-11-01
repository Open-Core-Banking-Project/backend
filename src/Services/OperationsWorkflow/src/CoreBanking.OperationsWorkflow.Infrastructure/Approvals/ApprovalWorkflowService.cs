using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.OperationsWorkflow.Application.Approvals;
using CoreBanking.OperationsWorkflow.Contracts;
using CoreBanking.OperationsWorkflow.Domain.Approvals;
using CoreBanking.OperationsWorkflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.OperationsWorkflow.Infrastructure.Approvals;

public sealed class ApprovalWorkflowService : IApprovalWorkflowService
{
    public const string Producer = "operations-workflow";
    public const string EventFamily = "approval";
    public const string DecisionEventType = "approval_decided";
    public const int SchemaVersion = 1;

    private readonly OperationsWorkflowDbContext _context;
    private readonly IApprovalEntitlementVerifier _entitlements;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public ApprovalWorkflowService(
        OperationsWorkflowDbContext context,
        IApprovalEntitlementVerifier entitlements,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ApprovalDecisionResult> DecideAsync(
        DecideApprovalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.DecisionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        if (_tenantContext.CurrentInstitution != command.InstitutionId)
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the approval request.");

        ApprovalRequestRecord record = await _context.ApprovalRequests
            .SingleOrDefaultAsync(x => x.RequestId == command.RequestId, cancellationToken).ConfigureAwait(false)
            ?? throw Reject("approval_request_not_found", "The approval request is not available in this institution.");
        ApprovalDecisionRecord? prior = await _context.ApprovalDecisions
            .SingleOrDefaultAsync(x => x.RequestId == record.RequestId, cancellationToken).ConfigureAwait(false);
        if (prior is not null)
        {
            if (!string.Equals(prior.DecisionId, command.DecisionId, StringComparison.Ordinal)
                || !string.Equals(prior.ApproverActorId, command.ApproverActorId.Value, StringComparison.Ordinal)
                || !string.Equals(prior.Decision, ToStorage(command.Decision), StringComparison.Ordinal))
                throw Reject("approval_request_already_decided", "The approval request already has a different final decision.");
            OutboxRecord outbox = await _context.OutboxMessages.SingleAsync(
                x => x.BusinessReference == record.RequestId && x.EventType == DecisionEventType,
                cancellationToken).ConfigureAwait(false);
            return Result(record, prior, outbox.EventId, true);
        }

        DateTimeOffset decidedAt = _timeProvider.GetUtcNow();
        EntitlementDecisionEvidence entitlement = await _entitlements.VerifyAsync(
            command.InstitutionId,
            command.ApproverActorId,
            record.RequiredPermission,
            decidedAt,
            cancellationToken).ConfigureAwait(false)
            ?? throw Reject("approval_permission_missing", "Identity and Access could not prove a current approval entitlement.");

        // The stored request names its profile; rebuilding with any other profile would enforce the wrong permission.
        ApprovalProfile profile = ApprovalProfiles.Find(record.WorkflowKey)
            ?? throw Reject("approval_profile_unknown", "The approval request names an unapproved workflow profile.");
        if (!string.Equals(record.WorkflowVersionId, profile.WorkflowVersionId, StringComparison.Ordinal)
            || !string.Equals(record.WorkflowDefinitionHash, profile.DefinitionHash, StringComparison.Ordinal))
        {
            throw Reject("approval_profile_mismatch", "The approval request does not match its approved profile version.");
        }

        ApprovalRequest aggregate = ApprovalRequest.Submit(
            profile,
            command.InstitutionId,
            record.RequestId,
            record.SourceRequestKey,
            record.SubjectId,
            record.SubjectVersion,
            record.SubjectHash,
            CoreBanking.BuildingBlocks.Domain.Identity.ActorId.From(record.MakerActorId),
            record.CorrelationId,
            record.SubmittedAt);
        ApprovalDecision decision;
        try
        {
            decision = aggregate.Decide(command.DecisionId, command.Decision, entitlement, command.Reason, decidedAt);
        }
        catch (InvalidOperationException exception)
        {
            throw new ApprovalWorkflowRejectedException(exception.Message, "The approval decision was rejected.", exception);
        }

        record.Status = ToStorage(aggregate.Status);
        record.DecidedAt = decision.DecidedAt;
        record.Version++;
        ApprovalDecisionRecord decisionRecord = new()
        {
            InstitutionId = record.InstitutionId,
            DecisionId = decision.DecisionId,
            RequestId = record.RequestId,
            Decision = ToStorage(decision.Decision),
            SubjectVersion = record.SubjectVersion,
            SubjectHash = record.SubjectHash,
            ApproverActorId = decision.Approver.Value,
            Permission = decision.Permission,
            EntitlementVersion = decision.EntitlementVersion,
            EntitlementValidFrom = decision.EntitlementValidFrom,
            EntitlementValidUntil = decision.EntitlementValidUntil,
            Reason = decision.Reason,
            DecidedAt = decision.DecidedAt,
        };
        _context.ApprovalDecisions.Add(decisionRecord);
        string eventId = AddDecisionOutbox(record, decisionRecord, command.CorrelationId);
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            throw new ApprovalWorkflowRejectedException(
                "approval_decision_conflict", "The approval request changed while the decision was being recorded.", exception);
        }

        return Result(record, decisionRecord, eventId, false);
    }

    private string AddDecisionOutbox(
        ApprovalRequestRecord request,
        ApprovalDecisionRecord decision,
        string correlationId)
    {
        string eventId = Guid.NewGuid().ToString("N");
        ApprovalDecidedV1 payload = new(
            request.InstitutionId, request.RequestId, decision.DecisionId, decision.Decision,
            request.WorkflowKey, request.WorkflowVersionId, request.WorkflowDefinitionHash, request.SubjectType, request.SubjectId,
            request.SubjectVersion, request.SubjectHash, decision.ApproverActorId, decision.Permission,
            decision.EntitlementVersion, decision.EntitlementValidFrom, decision.EntitlementValidUntil,
            decision.Reason, correlationId, decision.DecidedAt);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = DecisionEventType,
            SchemaVersion = SchemaVersion,
            OccurredAt = decision.DecidedAt,
            InstitutionId = request.InstitutionId,
            CorrelationId = correlationId,
            Producer = Producer,
            BusinessReference = request.RequestId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = request.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = DecisionEventType,
            SchemaVersion = SchemaVersion,
            Topic = EventContract.TopicFor(Producer, EventFamily, SchemaVersion),
            BusinessReference = request.RequestId,
            CorrelationId = correlationId,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = decision.DecidedAt,
            CreatedAt = decision.DecidedAt,
        });
        return eventId;
    }

    private static ApprovalDecisionResult Result(
        ApprovalRequestRecord request,
        ApprovalDecisionRecord decision,
        string eventId,
        bool replayed) => new(
            request.InstitutionId, request.RequestId, decision.DecisionId, decision.Decision,
            request.SubjectVersion, request.SubjectHash, eventId, decision.DecidedAt, replayed);

    private static string ToStorage(ApprovalRequestStatus status) => status switch
    {
        ApprovalRequestStatus.Submitted => "submitted",
        ApprovalRequestStatus.Approved => "approved",
        ApprovalRequestStatus.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static string ToStorage(ApprovalDecisionType decision) => decision switch
    {
        ApprovalDecisionType.Approve => "approved",
        ApprovalDecisionType.Reject => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(decision)),
    };

    private static ApprovalWorkflowRejectedException Reject(string code, string message) => new(code, message);
}
