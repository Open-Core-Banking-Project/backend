using System;
namespace CoreBanking.AuditReconciliation.Contracts;

public sealed record InstitutionSuspendedV1(string InstitutionId, string SuspendedByActorId, string Reason, DateTimeOffset SuspendedAt);
