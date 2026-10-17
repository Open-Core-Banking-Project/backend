namespace CoreBanking.AuditReconciliation.Contracts;

/// <summary>
/// Consumer-side version 1 shape for Identity Access' synthetic foundation event. Keep this
/// aligned with the producer contract; only stable identifiers cross the service boundary.
/// </summary>
public sealed record FoundationRecordCreatedV1(string FoundationRecordId, string ActorId);
