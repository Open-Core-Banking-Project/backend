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
