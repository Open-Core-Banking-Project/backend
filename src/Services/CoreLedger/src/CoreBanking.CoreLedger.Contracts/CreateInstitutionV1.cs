namespace CoreBanking.CoreLedger.Contracts;

public sealed record CreateInstitutionRequestV1(
    string? InstitutionId,
    string? Code,
    string? LegalName,
    string? CountryCode,
    string? BaseCurrency);

public sealed record CreateInstitutionResponseV1(
    string InstitutionId,
    string ProvisioningRunId,
    string EventId,
    bool Replayed,
    string Status);
