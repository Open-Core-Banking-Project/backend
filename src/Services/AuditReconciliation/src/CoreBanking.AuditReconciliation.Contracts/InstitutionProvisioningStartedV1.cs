namespace CoreBanking.AuditReconciliation.Contracts;

public sealed record InstitutionProvisioningStartedV1(
    string InstitutionId,
    int ProvisioningPlanVersion);
