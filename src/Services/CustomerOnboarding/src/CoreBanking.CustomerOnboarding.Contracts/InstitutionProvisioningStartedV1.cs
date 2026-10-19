namespace CoreBanking.CustomerOnboarding.Contracts;

public sealed record InstitutionProvisioningStartedV1(
    string InstitutionId,
    int ProvisioningPlanVersion);
