namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>
/// Stable codes for entitlement distribution. A consumer treats every one of these as a denial,
/// never as an empty permission set. Decision: ADR-0009; slice 9.
/// </summary>
public static class EntitlementDistributionV1
{
    public const string ActorMismatch = "identity.entitlement_actor_mismatch";
    public const string InstitutionMismatch = "identity.entitlement_institution_mismatch";
    public const string VersionRegressed = "identity.entitlement_version_regressed";
    public const string SnapshotIncomplete = "identity.entitlement_snapshot_incomplete";
}
