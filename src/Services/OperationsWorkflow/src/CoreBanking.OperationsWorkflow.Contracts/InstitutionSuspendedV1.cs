using System;
namespace CoreBanking.OperationsWorkflow.Contracts;
public sealed record InstitutionSuspendedV1(string InstitutionId, string SuspendedByActorId, string Reason, DateTimeOffset SuspendedAt);
