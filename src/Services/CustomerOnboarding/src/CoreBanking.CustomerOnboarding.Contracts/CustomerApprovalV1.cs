using System;
using System.Security.Cryptography;
using System.Text;

namespace CoreBanking.CustomerOnboarding.Contracts;

/// <summary>
/// The approved approval profile for a customer classification version. The seller service requests the
/// decision and the same profile is validated when the decision arrives. Decision: P1-E03-D09.
/// </summary>
public static class CustomerClassificationApprovalV1
{
    public const string WorkflowKey = "customer.classification.approve.v1";

    public const string WorkflowVersionId = "customer-classification-approval-v1";

    public const string SubjectType = "customer.classification.version";

    public const string SourceService = "customer-onboarding";

    public const string RequiredPermission = "customer.classification.approve";

    public const string EventFamily = "approval";

    public const string RequestedEventType = "approval_requested";

    public const string DecidedEventType = "approval_decided";

    public const int SchemaVersion = 1;

    public static string DefinitionHash { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{WorkflowKey}|{WorkflowVersionId}|one-level|maker-excluded|{RequiredPermission}|decision-time-entitlement")))
        .ToLowerInvariant();
}

/// <summary>
/// The approved approval profile for a customer merge plan. The merge request identifier is the approval
/// request identifier and the frozen plan hash is the subject hash, so a decision can only apply to the plan
/// the approver saw. Decision: P1-E03-D11.
/// </summary>
public static class CustomerMergeApprovalV1
{
    public const string WorkflowKey = "customer.merge.approve.v1";

    public const string WorkflowVersionId = "customer-merge-approval-v1";

    public const string SubjectType = "customer.merge.request";

    public const string SourceService = "customer-onboarding";

    public const string RequiredPermission = "customer.merge.approve";

    public static string DefinitionHash { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{WorkflowKey}|{WorkflowVersionId}|one-level|maker-excluded|{RequiredPermission}|decision-time-entitlement")))
        .ToLowerInvariant();
}

/// <summary>
/// The decision values Workflow publishes. Workflow stores and publishes <c>approved</c>/<c>rejected</c> (the
/// same values Core Ledger consumes), so every consumer must speak that vocabulary.
/// </summary>
public static class CustomerApprovalDecisions
{
    public const string Approved = "approved";

    public const string Rejected = "rejected";
}

/// <summary>
/// The acknowledgement this service publishes after applying a delivered decision. Workflow records it in
/// <c>approval.application_results</c>, so approval and application stay distinct states.
/// Decision: epic 06 invariants 5 and 16.
/// </summary>
public static class CustomerApprovalApplicationV1
{
    public const string SourceService = "customer-onboarding";

    public const string EventFamily = "approval";

    public const string EventType = "approval_application_result";

    public const string Applied = "applied";

    public const string Rejected = "rejected";
}

public sealed record CustomerApprovalApplicationResultV1(
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

/// <summary>The approval request this service publishes. Same shape as the Workflow contract.</summary>
public sealed record CustomerApprovalRequestedV1(
    string InstitutionId,
    string RequestId,
    string SourceRequestKey,
    string WorkflowKey,
    string WorkflowVersionId,
    string WorkflowDefinitionHash,
    string SourceService,
    string SubjectType,
    string SubjectId,
    string SubjectVersion,
    string SubjectHash,
    string MakerActorId,
    string RequiredPermission,
    string CorrelationId,
    DateTimeOffset SubmittedAt);

/// <summary>The decision this service consumes. Same shape as the Workflow contract.</summary>
public sealed record CustomerApprovalDecidedV1(
    string InstitutionId,
    string RequestId,
    string DecisionId,
    string Decision,
    string WorkflowKey,
    string WorkflowVersionId,
    string WorkflowDefinitionHash,
    string SubjectType,
    string SubjectId,
    string SubjectVersion,
    string SubjectHash,
    string ApproverActorId,
    string Permission,
    string EntitlementVersion,
    DateTimeOffset EntitlementValidFrom,
    DateTimeOffset EntitlementValidUntil,
    string? Reason,
    string CorrelationId,
    DateTimeOffset DecidedAt);
