using System;

namespace CoreBanking.CoreLedger.Contracts;

public sealed record SuspendInstitutionRequestV1(string Reason);
public sealed record SuspendInstitutionResponseV1(string InstitutionId, string EventId, DateTimeOffset SuspendedAt, bool Replayed, string Status);
public sealed record InstitutionSuspendedV1(string InstitutionId, string SuspendedByActorId, string Reason, DateTimeOffset SuspendedAt);
