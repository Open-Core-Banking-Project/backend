using System;
using System.Collections.Generic;

namespace CoreBanking.CoreLedger.Contracts;

public sealed record RepairInstitutionProvisioningRequestV1(string Reason);

public sealed record RepairInstitutionProvisioningResponseV1(
    string InstitutionId,
    string ProvisioningRunId,
    string SourceEventId,
    string AuditEventId,
    DateTimeOffset RequestedAt,
    bool Replayed,
    string Status);

public sealed record InstitutionProvisioningStepStatusV1(
    string ServiceId,
    string Status,
    string? FailureCode,
    DateTimeOffset UpdatedAt);

public sealed record InstitutionProvisioningStatusResponseV1(
    string InstitutionId,
    string ProvisioningRunId,
    int PlanVersion,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<InstitutionProvisioningStepStatusV1> Steps);
