using System;

namespace CoreBanking.BuildingBlocks.Application.Security;

/// <summary>
/// Stable denial reasons. The reason maps to a stable machine-readable code and status,
/// and never reveals whether a hidden resource exists. Decision: ADR-0009.
/// </summary>
public enum DenialReason
{
    None = 0,
    MissingAuthentication = 1,
    WrongInstitution = 2,
    MissingPermission = 3,
    ScopeMismatch = 4,
    StaleEntitlement = 5,
    StepUpRequired = 6,
    ServiceIdentityNotPermitted = 7,
    SegregationOfDuties = 8,
}

/// <summary>The outcome of an authorisation evaluation.</summary>
public sealed record AuthorisationDecision
{
    private AuthorisationDecision(bool isAllowed, DenialReason reason, PermissionIdentifier? permission, string? detail)
    {
        IsAllowed = isAllowed;
        Reason = reason;
        Permission = permission;
        Detail = detail;
    }

    public bool IsAllowed { get; }

    public DenialReason Reason { get; }

    public PermissionIdentifier? Permission { get; }

    /// <summary>Safe, non-sensitive explanation suitable for audit evidence and problem details.</summary>
    public string? Detail { get; }

    public static AuthorisationDecision Allowed(PermissionIdentifier permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return new AuthorisationDecision(true, DenialReason.None, permission, null);
    }

    public static AuthorisationDecision Denied(DenialReason reason, string? detail = null, PermissionIdentifier? permission = null)
    {
        if (reason == DenialReason.None)
        {
            throw new ArgumentException("A denial must state a reason.", nameof(reason));
        }

        return new AuthorisationDecision(false, reason, permission, detail);
    }
}

/// <summary>Everything the policy needs to evaluate one decision.</summary>
public sealed record AuthorisationRequest
{
    public required PermissionRequirement Requirement { get; init; }

    public required ResourceScope Scope { get; init; }

    /// <summary>The resolved actor, or <c>null</c> when authentication did not succeed.</summary>
    public AuthenticatedActor? Actor { get; init; }

    /// <summary>Actor that initiated a restricted action, used for segregation of duties.</summary>
    public string? MakerActorId { get; init; }

    /// <summary>Whether a fresh step-up authentication has been performed for this request.</summary>
    public bool StepUpSatisfied { get; init; }
}
