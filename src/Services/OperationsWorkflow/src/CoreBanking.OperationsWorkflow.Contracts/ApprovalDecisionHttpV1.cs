using System;

namespace CoreBanking.OperationsWorkflow.Contracts;

public sealed record DecideApprovalRequestV1(
    string Decision,
    string? Reason);

public sealed record ApprovalDecisionResponseV1(
    string InstitutionId,
    string RequestId,
    string DecisionId,
    string Decision,
    string SubjectVersion,
    string SubjectHash,
    string EventId,
    DateTimeOffset DecidedAt,
    bool Replayed);
