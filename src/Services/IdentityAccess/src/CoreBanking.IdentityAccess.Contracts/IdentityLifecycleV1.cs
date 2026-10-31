namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>Optional provider subject attached during registration. Raw subjects are never returned.</summary>
public sealed record ProviderIdentityLinkV1(string Provider, string Realm, string Subject);

/// <summary>
/// Registers an identity in the authenticated institution. Institution, identity, membership and
/// lifecycle status are intentionally absent to prevent mass assignment.
/// </summary>
public sealed record RegisterIdentityV1(
    string IdentityType,
    string DisplayName,
    ProviderIdentityLinkV1? ProviderLink);

/// <summary>Safe result of an idempotent identity registration.</summary>
public sealed record IdentityRegistrationResultV1(
    string IdentityId,
    string MembershipId,
    string IdentityType,
    string Status,
    bool ProviderLinked,
    string EventId,
    bool Replayed);

/// <summary>Tenant-scoped identity view. Provider subjects and internal fingerprints are excluded.</summary>
public sealed record IdentityDetailsV1(
    string IdentityId,
    string MembershipId,
    string IdentityType,
    string DisplayName,
    string Status,
    bool ProviderLinked,
    long EntitlementVersion);

/// <summary>Version 1 fact emitted after identity, membership and optional provider link commit.</summary>
public sealed record IdentityRegisteredV1(
    string IdentityId,
    string MembershipId,
    string IdentityType,
    string Status,
    bool ProviderLinked);

/// <summary>Status command with an optimistic version. Institution and actor are trusted context.</summary>
public sealed record ChangeIdentityStatusV1(string Status, string Reason, long ExpectedVersion);

public sealed record IdentityStatusChangeResultV1(
    string IdentityId,
    string PreviousStatus,
    string Status,
    long EntitlementVersion,
    string EventId,
    bool Replayed);

public sealed record IdentityStatusChangedV1(
    string IdentityId,
    string MembershipId,
    string PreviousStatus,
    string Status,
    long EntitlementVersion,
    string ReasonCode);
