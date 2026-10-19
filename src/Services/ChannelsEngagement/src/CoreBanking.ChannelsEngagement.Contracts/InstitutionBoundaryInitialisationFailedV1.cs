namespace CoreBanking.ChannelsEngagement.Contracts;

public sealed record InstitutionBoundaryInitialisationFailedV1(
    string InstitutionId,
    string ServiceId,
    int ProvisioningPlanVersion,
    string SourceEventId,
    string FailureCode);
