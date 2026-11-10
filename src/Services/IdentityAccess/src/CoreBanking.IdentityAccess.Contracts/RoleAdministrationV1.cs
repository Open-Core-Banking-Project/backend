using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>Stable values for role drafting. Decision: ADR-0009, P1-E02-D09 and P1-E02-D10.</summary>
public static class RoleContractV1
{
    public const int SchemaVersion = IdentityAccessContractV1.SchemaVersion;

    public const string StatusDraft = "draft";
    public const string StatusPendingApproval = "pending_approval";
    public const string Simulated = "simulated";

    public const string InvalidRole = "request.invalid_role";
    public const string DuplicateRole = "identity.role_exists";
    public const string RoleNotFound = "identity.role_not_found";
    public const string RoleNotDraft = "identity.role_not_editable";
    public const string PermissionNotInCatalogue = "identity.permission_not_found";
    public const string RoleConflictsWithItself = "identity.role_conflict";
    public const string RoleAlreadySubmitted = "identity.role_already_submitted";
    public const string RoleConcurrencyConflict = "identity.role_concurrency_conflict";
}

/// <summary>The frozen one-level maker-checker profile used for role publication.</summary>
public static class RolePublicationApprovalV1
{
    public const string WorkflowKey = "identity.role.publish.v1";
    public const string WorkflowVersionId = "identity-role-publication-v1";
    public const string SubjectType = "identity.role.version";
    public const string SourceService = "identity-access";
    public const string RequiredPermission = "identity.permission.assign";
    public const string EventFamily = "approval";
    public const string RequestedEventType = "approval_requested";
    public const string DecidedEventType = "approval_decided";
    public const string PublishedEventType = "role_version_published";
    public const string ApplicationResultEventType = "approval_application_result";

    public static string DefinitionHash { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{WorkflowKey}|{WorkflowVersionId}|one-level|maker-excluded|{RequiredPermission}|decision-time-entitlement")))
        .ToLowerInvariant();
}

/// <summary>Workflow-compatible request for a decision on one frozen role version.</summary>
public sealed record RoleApprovalRequestedV1(
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

public sealed record RoleApprovalDecidedV1(
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

public sealed record RoleApprovalApplicationResultV1(
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

public sealed record RoleVersionPublishedV1(
    string InstitutionId,
    string RoleId,
    string RoleCode,
    long Version,
    string ContentHash,
    string ApprovalRequestId,
    string WorkflowDecisionId,
    string PublishedByActorId,
    DateTimeOffset PublishedAt);

/// <summary>A role draft is inert and cannot be granted until it is published.</summary>
public sealed record RoleDraftRequestV1
{
    public int SchemaVersion { get; init; } = RoleContractV1.SchemaVersion;

    public required string RoleCode { get; init; }

    public required string RoleName { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }
}

/// <summary>Updates a draft in place. The code is immutable once created.</summary>
public sealed record RoleUpdateRequestV1
{
    public int SchemaVersion { get; init; } = RoleContractV1.SchemaVersion;

    public required string RoleName { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }
}

/// <summary>Freezes the current draft and submits its exact version to Workflow.</summary>
public sealed record RoleSubmissionRequestV1
{
    public int SchemaVersion { get; init; } = RoleContractV1.SchemaVersion;
}

public sealed record RoleSubmissionResultV1
{
    public int SchemaVersion { get; init; } = RoleContractV1.SchemaVersion;

    public required string RoleId { get; init; }

    public required long Version { get; init; }

    public required string Status { get; init; }

    public required string ApprovalRequestId { get; init; }

    public required string ContentHash { get; init; }
}

/// <summary>Simulates a proposed permission set before it is saved.</summary>
public sealed record RoleSimulationRequestV1
{
    public int SchemaVersion { get; init; } = RoleContractV1.SchemaVersion;

    public required IReadOnlyList<string> Permissions { get; init; }
}

public sealed record RoleDraftResultV1
{
    public int SchemaVersion { get; init; } = RoleContractV1.SchemaVersion;

    public required string RoleId { get; init; }

    public required string RoleCode { get; init; }

    public required string RoleName { get; init; }

    public required long Version { get; init; }

    public required string Status { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }
}

/// <summary>One prohibited pair inside the simulated permission set.</summary>
public sealed record RoleConflictV1
{
    public required string PermissionCode { get; init; }

    public required string ConflictingPermissionCode { get; init; }
}

/// <summary>
/// The simulation answer. <see cref="IsPublishable"/> is true only when every code is in the
/// catalogue, the set is non-empty and no prohibited pair is present.
/// </summary>
public sealed record RoleSimulationResultV1
{
    public int SchemaVersion { get; init; } = RoleContractV1.SchemaVersion;

    public required string Status { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }

    public required IReadOnlyList<string> UnknownPermissions { get; init; }

    public required IReadOnlyList<RoleConflictV1> Conflicts { get; init; }

    public required bool IsPublishable { get; init; }
}
