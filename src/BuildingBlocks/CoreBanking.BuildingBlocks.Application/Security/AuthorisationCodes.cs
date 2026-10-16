using System;

namespace CoreBanking.BuildingBlocks.Application.Security;

/// <summary>
/// The published mapping from a denial reason to a machine-readable code and an HTTP
/// status. Missing authentication is 401; every other refusal is 403. Codes are stable
/// and never disclose whether a hidden resource exists. Decision: ADR-0009, epic 03.
/// </summary>
public static class AuthorisationCodes
{
    public const string MissingAuthentication = "auth.missing_authentication";
    public const string WrongInstitution = "auth.wrong_institution";
    public const string MissingPermission = "auth.missing_permission";
    public const string ScopeMismatch = "auth.scope_mismatch";
    public const string StaleEntitlement = "auth.stale_entitlement";
    public const string StepUpRequired = "auth.step_up_required";
    public const string ServiceIdentityNotPermitted = "auth.service_identity_not_permitted";
    public const string SegregationOfDuties = "auth.segregation_of_duties";

    public static string For(DenialReason reason) => reason switch
    {
        DenialReason.MissingAuthentication => MissingAuthentication,
        DenialReason.WrongInstitution => WrongInstitution,
        DenialReason.MissingPermission => MissingPermission,
        DenialReason.ScopeMismatch => ScopeMismatch,
        DenialReason.StaleEntitlement => StaleEntitlement,
        DenialReason.StepUpRequired => StepUpRequired,
        DenialReason.ServiceIdentityNotPermitted => ServiceIdentityNotPermitted,
        DenialReason.SegregationOfDuties => SegregationOfDuties,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "A stable code is required for every denial reason."),
    };

    public static int StatusFor(DenialReason reason) =>
        reason == DenialReason.MissingAuthentication ? 401 : 403;

    public static string TitleFor(DenialReason reason) => reason switch
    {
        DenialReason.MissingAuthentication => "Authentication required",
        DenialReason.WrongInstitution => "Not permitted in this institution",
        DenialReason.MissingPermission => "Permission required",
        DenialReason.ScopeMismatch => "Not permitted for this scope",
        DenialReason.StaleEntitlement => "Entitlements must be refreshed",
        DenialReason.StepUpRequired => "Step-up authentication required",
        DenialReason.ServiceIdentityNotPermitted => "Not permitted for this identity type",
        DenialReason.SegregationOfDuties => "Segregation of duties",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "A title is required for every denial reason."),
    };
}
