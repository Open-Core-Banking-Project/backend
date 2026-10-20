using System;
namespace CoreBanking.IntegrationHub.Contracts;

public sealed record InstitutionSuspendedV1(string InstitutionId, string SuspendedByActorId, string Reason, DateTimeOffset SuspendedAt);
