namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>Controlled failure evidence for one local institution-boundary initialisation.</summary>
public sealed record InstitutionBoundaryInitialisationFailedV1(
    string InstitutionId,
    string ServiceId,
    int ProvisioningPlanVersion,
    string SourceEventId,
    string FailureCode);
