using System;
using System.Collections.Generic;

namespace CoreBanking.OperationsWorkflow.Api.Authorisation;

/// <summary>Private wire projections of the Identity version 1 contracts consumed by Workflow.</summary>
public sealed record EntitlementSnapshotRequestV1
{
    public int SchemaVersion { get; init; } = 1;
    public required string ActorId { get; init; }
    public required string InstitutionId { get; init; }
    public required long MinimumVersion { get; init; }
}

public sealed record EntitlementSnapshotV1
{
    public int SchemaVersion { get; init; } = 1;
    public required string ActorId { get; init; }
    public required string InstitutionId { get; init; }
    public required long Version { get; init; }
    public required bool IsComplete { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required IReadOnlyList<PermissionGrantV1> Grants { get; init; }
}

public sealed record PermissionGrantV1
{
    public required string Permission { get; init; }
    public required GrantScopeV1 Scope { get; init; }
    public DateTimeOffset? ValidFrom { get; init; }
    public DateTimeOffset? ValidTo { get; init; }
}

public sealed record GrantScopeV1
{
    public required string InstitutionId { get; init; }
    public string? LegalEntityId { get; init; }
    public string? RegionId { get; init; }
    public string? BranchId { get; init; }
    public string? ChannelId { get; init; }
    public string? CustomerId { get; init; }
    public string? AccountId { get; init; }
    public string? MandateId { get; init; }
    public string? Currency { get; init; }
    public decimal? MaximumAmount { get; init; }
    public string? DeviceId { get; init; }
    public string? MinimumAuthenticationStrength { get; init; }
}

public static class ApprovalEligibilityOutcomeV1
{
    public const string Eligible = "eligible";
}

public static class ApprovalEligibilityCodeV1
{
    public const string Eligible = "approval.eligible";
    public const string SegregationOfDuties = "auth.segregation_of_duties";
}

public sealed record ServiceApprovalEligibilityRequestV1
{
    public int SchemaVersion { get; init; } = 1;
    public required string InstitutionId { get; init; }
    public required string SourceSystem { get; init; }
    public required string SourceRequestId { get; init; }
    public required string RequiredPermission { get; init; }
    public required string MakerActorId { get; init; }
    public required string CandidateActorId { get; init; }
}

public sealed record ApprovalEligibilityResponseV1
{
    public int SchemaVersion { get; init; } = 1;
    public required string Outcome { get; init; }
    public required string Code { get; init; }
    public required string RequiredPermission { get; init; }
    public required string SourceSystem { get; init; }
    public required string SourceRequestId { get; init; }
    public required bool MakerSeparated { get; init; }
    public required long EvaluatedEntitlementVersion { get; init; }
    public required DateTimeOffset EvaluatedAt { get; init; }
}
