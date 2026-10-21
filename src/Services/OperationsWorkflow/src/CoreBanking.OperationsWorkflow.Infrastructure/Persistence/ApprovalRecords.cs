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
