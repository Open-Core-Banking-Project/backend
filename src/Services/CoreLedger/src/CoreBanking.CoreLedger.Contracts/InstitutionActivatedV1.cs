using System;

namespace CoreBanking.CoreLedger.Contracts;

public sealed record InstitutionActivatedV1(
    string InstitutionId,
    string ProvisioningRunId,
    int ProvisioningPlanVersion,
    string ActivatedByActorId,
    DateTimeOffset ActivatedAt);
