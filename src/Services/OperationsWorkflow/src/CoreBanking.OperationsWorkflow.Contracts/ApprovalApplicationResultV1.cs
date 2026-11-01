using System;

namespace CoreBanking.OperationsWorkflow.Contracts;

/// <summary>
/// The source-service acknowledgement. Approval and application are different states: a request is
/// <c>applied</c> only after the owning service reports a positive, correlated, idempotent result for the exact
/// decision and subject version/hash. Decision: epic 06 invariants 5 and 16, and the state machine in section 6.
/// </summary>
public static class ApprovalApplicationResultV1Codes
{
    public const string EventFamily = "approval";

    public const string EventType = "approval_application_result";

    public const int SchemaVersion = 1;

    /// <summary>The source applied the approved change.</summary>
    public const string Applied = "applied";

    /// <summary>The source recorded the rejection; there was nothing to apply.</summary>
    public const string Rejected = "rejected";

    /// <summary>The source positively could not apply the decision. Retry uses the same decision and hash.</summary>
    public const string ApplyFailed = "apply_failed";
}

/// <summary>One acknowledgement from the owning service for one delivered decision.</summary>
public sealed record ApprovalApplicationResultV1(
    string InstitutionId,
    string RequestId,
    string DecisionId,
    string ResultId,
    string WorkflowKey,
    string SubjectType,
    string SubjectId,
    string SubjectVersion,
    string SubjectHash,
    string SourceService,
    string Outcome,
    string? FailureCode,
    string? Reason,
    string CorrelationId,
    DateTimeOffset AcknowledgedAt);
