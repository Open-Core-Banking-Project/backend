using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.OperationsWorkflow.Domain.Approvals;

namespace CoreBanking.OperationsWorkflow.Application.Approvals;

public sealed record DecideApprovalCommand(
    InstitutionId InstitutionId,
    ActorId ApproverActorId,
    string RequestId,
    string DecisionId,
    ApprovalDecisionType Decision,
    string? Reason,
    string CorrelationId);

public sealed record ApprovalDecisionResult(
    string InstitutionId,
    string RequestId,
    string DecisionId,
    string Decision,
    string SubjectVersion,
    string SubjectHash,
    string EventId,
    DateTimeOffset DecidedAt,
    bool Replayed);

public interface IApprovalEntitlementVerifier
{
    Task<EntitlementDecisionEvidence?> VerifyAsync(
        InstitutionId institutionId,
        ActorId actorId,
        string requiredPermission,
        DateTimeOffset decisionTime,
        CancellationToken cancellationToken);
}

public sealed record ApprovalEligibilityVerification(bool IsEligible, string Code, long EvaluatedEntitlementVersion);

/// <summary>
/// Identity-owned decision-time check. Workflow supplies the maker stored on the request and the
/// candidate resolved from trusted authentication; it never calculates eligibility itself.
/// </summary>
public interface IApprovalEligibilityVerifier
{
    Task<ApprovalEligibilityVerification> VerifyAsync(
        InstitutionId institutionId,
        ActorId candidateActorId,
        ActorId makerActorId,
        string sourceRequestId,
        string requiredPermission,
        CancellationToken cancellationToken);
}

public interface IApprovalWorkflowService
{
    Task<ApprovalDecisionResult> DecideAsync(
        DecideApprovalCommand command,
        CancellationToken cancellationToken);
}

public sealed class ApprovalWorkflowRejectedException : Exception
{
    public ApprovalWorkflowRejectedException(string code, string message) : base(message) => Code = code;
    public ApprovalWorkflowRejectedException(string code, string message, Exception inner) : base(message, inner) => Code = code;
    public string Code { get; }
}
