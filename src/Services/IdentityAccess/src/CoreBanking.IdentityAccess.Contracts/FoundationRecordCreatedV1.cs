namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>Version 1 payload for the synthetic Phase 0 foundation-record event.</summary>
public sealed record FoundationRecordCreatedV1(string FoundationRecordId, string ActorId);
