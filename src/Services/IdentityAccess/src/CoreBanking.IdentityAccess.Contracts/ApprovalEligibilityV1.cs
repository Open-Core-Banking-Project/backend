using System;

namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>Stable outcome values for a version 1 approver eligibility answer.</summary>
public static class ApprovalEligibilityOutcomeV1
{
    public const string Eligible = "eligible";
    public const string Ineligible = "ineligible";
}

/// <summary>
/// Stable machine-readable codes. They match <c>AuthorisationCodes</c> so a source service never
/// has to translate two vocabularies, and they never disclose whether a hidden resource exists.
/// Decision: ADR-0009; epic 02 slice 10.
/// </summary>
public static class ApprovalEligibilityCodeV1
{
    public const string Eligible = "approval.eligible";
    public const string MissingAuthentication = "auth.missing_authentication";
    public const string WrongInstitution = "auth.wrong_institution";
    public const string MissingPermission = "auth.missing_permission";
    public const string ScopeMismatch = "auth.scope_mismatch";
    public const string StaleEntitlement = "auth.stale_entitlement";
    public const string ServiceIdentityNotPermitted = "auth.service_identity_not_permitted";
    public const string SegregationOfDuties = "auth.segregation_of_duties";
}

/// <summary>
/// Identity-side question asked by an approval source such as Operations and Workflow: may the
/// authenticated actor decide the source request, given that a different actor made it?
/// The caller is always the candidate approver. <see cref="MakerActorId"/> must come from the
/// source's stored request record, never from the approver, so a source-authenticated variant is
/// required before Workflow can ask on another actor's behalf.
/// </summary>
public sealed record ApprovalEligibilityRequestV1
{
    public int SchemaVersion { get; init; } = IdentityAccessContractV1.SchemaVersion;

    /// <summary>The institution the source request belongs to. It must match the authenticated context.</summary>
    public required string InstitutionId { get; init; }

    /// <summary>Source service name, for example <c>operations_workflow</c>. Evidence only.</summary>
    public required string SourceSystem { get; init; }

    /// <summary>The source approval request identity. Evidence only; never a local resource key.</summary>
    public required string SourceRequestId { get; init; }

    /// <summary>The stable capability the decision requires, in <c>domain.resource.action</c> form.</summary>
    public required string RequiredPermission { get; init; }

    /// <summary>The stable identity of the actor that raised the source request.</summary>
    public required string MakerActorId { get; init; }
}

/// <summary>
/// Data-minimised answer. It states whether the caller may decide the request and why, and never
/// returns the maker's grants, roles, session or personal data.
/// </summary>
public sealed record ApprovalEligibilityResponseV1
{
    public int SchemaVersion { get; init; } = IdentityAccessContractV1.SchemaVersion;

    public required string Outcome { get; init; }

    public required string Code { get; init; }

    public required string RequiredPermission { get; init; }

    public required string SourceSystem { get; init; }

    public required string SourceRequestId { get; init; }

    /// <summary>True only when the caller is a different stable identity from the maker.</summary>
    public required bool MakerSeparated { get; init; }

    public required long EvaluatedEntitlementVersion { get; init; }

    public required DateTimeOffset EvaluatedAt { get; init; }
}
