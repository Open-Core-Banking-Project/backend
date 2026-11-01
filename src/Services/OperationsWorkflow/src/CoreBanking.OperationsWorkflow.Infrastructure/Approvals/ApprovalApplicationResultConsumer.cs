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

/// <summary>
/// Records the owning service's acknowledgement of a delivered decision. The request moves to <c>applied</c>,
/// <c>rejected</c> or <c>apply_failed</c>; it is never reported as applied without this fact, and a duplicate
/// acknowledgement changes nothing.
/// Decision: epic 06 invariant 5 and 16, sections 6 and 8.
/// </summary>
public sealed class ApprovalApplicationResultConsumer : IIntegrationEventConsumer
{
    public const string ConsumerName = "operations-workflow.approval-application-result";

    private readonly OperationsWorkflowDbContext _context;
    private readonly InboxProcessor _inbox;
    private readonly AmbientTenantContext _tenantContext;

    public ApprovalApplicationResultConsumer(
        OperationsWorkflowDbContext context,
        InboxProcessor inbox,
        AmbientTenantContext tenantContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public string Topic => throw new NotSupportedException("The acknowledgement consumer subscribes to every approved source topic.");

    public async Task<InboxResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _tenantContext.Clear();
        _tenantContext.Establish(InstitutionId.From(envelope.InstitutionId));
        try
        {
            return await _inbox.ProcessAsync(
                ConsumerName,
                envelope,
                ApprovalApplicationResultV1Codes.SchemaVersion,
                token => AcknowledgeAsync(envelope, token),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _tenantContext.Clear();
        }
    }

    private async Task<string?> AcknowledgeAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!string.Equals(envelope.EventType, ApprovalApplicationResultV1Codes.EventType, StringComparison.Ordinal))
            throw new InvalidOperationException("The acknowledgement topic contained an unexpected event type.");

        ApprovalApplicationResultV1 payload = EventContract.Deserialize<ApprovalApplicationResultV1>(envelope.Payload)
            ?? throw new InvalidOperationException("The acknowledgement payload is missing.");
        Validate(envelope, payload);

        ApprovalRequestRecord request = await _context.ApprovalRequests
            .SingleOrDefaultAsync(x => x.InstitutionId == payload.InstitutionId && x.RequestId == payload.RequestId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The acknowledgement cites an approval request this service does not hold.");

        if (!string.Equals(request.SourceService, payload.SourceService, StringComparison.Ordinal)
            || !string.Equals(request.WorkflowKey, payload.WorkflowKey, StringComparison.Ordinal)
            || !string.Equals(request.SubjectType, payload.SubjectType, StringComparison.Ordinal)
            || !string.Equals(request.SubjectVersion, payload.SubjectVersion, StringComparison.Ordinal)
            || !string.Equals(request.SubjectHash, payload.SubjectHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The acknowledgement does not match the stored approval request binding.");
        }

        ApprovalDecisionRecord? decision = await _context.ApprovalDecisions
            .SingleOrDefaultAsync(x => x.InstitutionId == payload.InstitutionId && x.RequestId == payload.RequestId, cancellationToken)
            .ConfigureAwait(false);
        if (decision is null || !string.Equals(decision.DecisionId, payload.DecisionId, StringComparison.Ordinal))
            throw new InvalidOperationException("The acknowledgement cites a decision this request does not hold.");

        ApprovalApplicationResultRecord? existing = await _context.ApprovalApplicationResults
            .SingleOrDefaultAsync(x => x.InstitutionId == payload.InstitutionId && x.ResultId == payload.ResultId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return "already_acknowledged:" + existing.ResultId;
        }

        string outcome = Normalise(payload.Outcome);
        if (outcome == ApprovalApplicationResultV1Codes.Applied
            && !string.Equals(decision.Decision, "approved", StringComparison.Ordinal))
            throw new InvalidOperationException("A rejected decision cannot be acknowledged as applied.");
        if (outcome == ApprovalApplicationResultV1Codes.ApplyFailed && string.IsNullOrWhiteSpace(payload.FailureCode))
            throw new InvalidOperationException("A failed application must report a failure code.");

        _context.ApprovalApplicationResults.Add(new ApprovalApplicationResultRecord
        {
            InstitutionId = payload.InstitutionId,
            ResultId = payload.ResultId,
            RequestId = payload.RequestId,
            DecisionId = payload.DecisionId,
            Outcome = outcome,
            FailureCode = payload.FailureCode,
            Reason = payload.Reason,
            SourceService = payload.SourceService,
            SubjectVersion = payload.SubjectVersion,
            SubjectHash = payload.SubjectHash,
            CorrelationId = payload.CorrelationId,
            AcknowledgedAt = payload.AcknowledgedAt,
        });

        request.ApplicationStatus = outcome;
        request.AppliedAt = outcome == ApprovalApplicationResultV1Codes.Applied ? payload.AcknowledgedAt : null;
        request.ApplyFailureCode = payload.FailureCode;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return outcome + ":" + payload.ResultId;
    }

    private static string Normalise(string outcome)
    {
        if (string.Equals(outcome, ApprovalApplicationResultV1Codes.Applied, StringComparison.Ordinal))
        {
            return ApprovalApplicationResultV1Codes.Applied;
        }

        if (string.Equals(outcome, ApprovalApplicationResultV1Codes.Rejected, StringComparison.Ordinal))
        {
            return ApprovalApplicationResultV1Codes.Rejected;
        }

        if (string.Equals(outcome, ApprovalApplicationResultV1Codes.ApplyFailed, StringComparison.Ordinal))
        {
            return ApprovalApplicationResultV1Codes.ApplyFailed;
        }

        throw new InvalidOperationException("The acknowledgement outcome is not approved.");
    }

    private static void Validate(IntegrationEventEnvelope envelope, ApprovalApplicationResultV1 payload)
    {
        if (!string.Equals(payload.InstitutionId, envelope.InstitutionId, StringComparison.Ordinal)
            || !string.Equals(payload.RequestId, envelope.BusinessReference, StringComparison.Ordinal)
            || !string.Equals(payload.CorrelationId, envelope.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(payload.SourceService, envelope.Producer, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(payload.ResultId)
            || string.IsNullOrWhiteSpace(payload.DecisionId))
            throw new InvalidOperationException("The acknowledgement does not match its trusted envelope.");
    }
}
