namespace CoreBanking.PaymentsVtu.Contracts;

public sealed record InstitutionBoundaryInitialisedV1(
    string InstitutionId,
    string ServiceId,
    int ProvisioningPlanVersion,
    string SourceEventId);
