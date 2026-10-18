namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>Consumer-side version 1 shape of Core Ledger's provisioning-started event.</summary>
public sealed record InstitutionProvisioningStartedV1(
    string InstitutionId,
    int ProvisioningPlanVersion);
