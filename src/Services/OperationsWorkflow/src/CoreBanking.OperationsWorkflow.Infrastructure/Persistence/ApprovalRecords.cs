using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.OperationsWorkflow.Infrastructure.Persistence;

public sealed class ApprovalRequestRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string RequestId { get; set; }
    public required string SourceRequestKey { get; set; }
    public required string WorkflowKey { get; set; }
    public required string WorkflowVersionId { get; set; }
    public required string WorkflowDefinitionHash { get; set; }
    public required string SourceService { get; set; }
    public required string SubjectType { get; set; }
    public required string SubjectId { get; set; }
    public required string SubjectVersion { get; set; }
    public required string SubjectHash { get; set; }
    public required string MakerActorId { get; set; }
    public required string RequiredPermission { get; set; }
    public required string CorrelationId { get; set; }
    public required string Status { get; set; }
    public long Version { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>
    /// <c>null</c> until the owning service acknowledges the delivered decision, then <c>applied</c>,
    /// <c>rejected</c> or <c>apply_failed</c>. Approval is never treated as application.
    /// </summary>
    public string? ApplicationStatus { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }

    public string? ApplyFailureCode { get; set; }
}

/// <summary>
/// One source-service acknowledgement of one delivered decision. It is append-only evidence: the unique result
/// identifier makes retries idempotent and the row is never rewritten. Decision: epic 06 section 11.
/// </summary>
public sealed class ApprovalApplicationResultRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string ResultId { get; set; }
    public required string RequestId { get; set; }
    public required string DecisionId { get; set; }
    public required string Outcome { get; set; }
    public string? FailureCode { get; set; }
    public string? Reason { get; set; }
    public required string SourceService { get; set; }
    public required string SubjectVersion { get; set; }
    public required string SubjectHash { get; set; }
    public required string CorrelationId { get; set; }
    public DateTimeOffset AcknowledgedAt { get; set; }
}

public sealed class ApprovalDecisionRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string DecisionId { get; set; }
    public required string RequestId { get; set; }
    public required string Decision { get; set; }
    public required string SubjectVersion { get; set; }
    public required string SubjectHash { get; set; }
    public required string ApproverActorId { get; set; }
    public required string Permission { get; set; }
    public required string EntitlementVersion { get; set; }
    public DateTimeOffset EntitlementValidFrom { get; set; }
    public DateTimeOffset EntitlementValidUntil { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
}
