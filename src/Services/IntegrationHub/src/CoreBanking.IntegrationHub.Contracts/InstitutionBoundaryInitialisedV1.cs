namespace CoreBanking.IntegrationHub.Contracts;

public sealed record InstitutionBoundaryInitialisedV1(
    string InstitutionId,
    string ServiceId,
    int ProvisioningPlanVersion,
    string SourceEventId);
