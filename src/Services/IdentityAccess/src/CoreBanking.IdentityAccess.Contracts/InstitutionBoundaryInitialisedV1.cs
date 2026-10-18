namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>Identity and Access acknowledgement that its local institution boundary is durable.</summary>
public sealed record InstitutionBoundaryInitialisedV1(
    string InstitutionId,
    string ServiceId,
    int ProvisioningPlanVersion,
    string SourceEventId);
