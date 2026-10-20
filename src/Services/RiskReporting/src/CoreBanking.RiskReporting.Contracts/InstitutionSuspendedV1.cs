using System;
namespace CoreBanking.RiskReporting.Contracts;

public sealed record InstitutionSuspendedV1(string InstitutionId, string SuspendedByActorId, string Reason, DateTimeOffset SuspendedAt);
