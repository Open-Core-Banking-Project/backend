using System;

namespace CoreBanking.CoreLedger.Contracts;

public sealed record ActivateInstitutionResponseV1(
    string InstitutionId,
    string ProvisioningRunId,
    string EventId,
    DateTimeOffset ActivatedAt,
    bool Replayed,
    string Status);
