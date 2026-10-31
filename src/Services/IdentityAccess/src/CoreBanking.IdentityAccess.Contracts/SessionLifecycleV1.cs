using System;

namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>Safe result of a local session revocation; provider tokens and references are never returned.</summary>
public sealed record SessionRevocationResultV1(string SessionId, string Status, string EventId, bool Replayed);

/// <summary>Safe, local session metadata. Provider tokens and family references are never exposed.</summary>
public sealed record SessionDetailsV1(
    string SessionId,
    string Status,
    string? DeviceId,
    string AuthenticationStrength,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset LastActivityAt);
