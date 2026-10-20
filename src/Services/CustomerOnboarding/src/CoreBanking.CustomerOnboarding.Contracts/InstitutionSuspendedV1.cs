using System;
namespace CoreBanking.CustomerOnboarding.Contracts;

public sealed record InstitutionSuspendedV1(string InstitutionId, string SuspendedByActorId, string Reason, DateTimeOffset SuspendedAt);
