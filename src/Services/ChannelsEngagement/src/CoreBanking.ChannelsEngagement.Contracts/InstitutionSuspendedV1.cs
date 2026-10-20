using System;
namespace CoreBanking.ChannelsEngagement.Contracts;

public sealed record InstitutionSuspendedV1(string InstitutionId, string SuspendedByActorId, string Reason, DateTimeOffset SuspendedAt);
