using System;
using CoreBanking.BuildingBlocks.Application.Persistence;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.IdentityAccess.Domain.Identities;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Identity;

/// <summary>
/// A human or service actor registered in one institution. Credentials and protocol-specific
/// storage stay with the identity provider; this table holds only the platform's own record.
/// Table: <c>identity.identities</c>. Decision: ADR-0004, ADR-0008.
/// </summary>
public sealed class IdentityRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string IdentityId { get; set; }

    public ActorType ActorType { get; set; }

    public IdentityKind IdentityType { get; set; }

    public required string DisplayName { get; set; }

    public IdentityState Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long ConcurrencyVersion { get; set; }
}

/// <summary>
/// Institution-scoped access relationship for a platform identity. All tenant access paths resolve
/// this membership before returning identity data. Table: <c>identity.institution_memberships</c>.
/// </summary>
public sealed class InstitutionMembershipRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string MembershipId { get; set; }

    public required string IdentityId { get; set; }

    public IdentityState Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long ConcurrencyVersion { get; set; }
}

/// <summary>
/// Provider/realm subject mapping. Only a one-way subject fingerprint is persisted; the raw
/// provider subject is used transiently during registration. Table: <c>identity.identity_links</c>.
/// </summary>
public sealed class ProviderIdentityLinkRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string IdentityLinkId { get; set; }

    public required string IdentityId { get; set; }

    public required string Provider { get; set; }

    public required string Realm { get; set; }

    public required string SubjectFingerprint { get; set; }

    public RecordStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A non-human caller such as another service, with the audience it may present.
/// Table: <c>identity.service_clients</c>. Decision: ADR-0008.
/// </summary>
public sealed class ServiceClientRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string ServiceClientId { get; set; }

    public required string ClientName { get; set; }

    public required string Audience { get; set; }

    /// <summary>Opaque provider reference only; the provider holds the secret. Never logged.</summary>
    public required string CredentialReference { get; set; }

    public DateTimeOffset? CredentialExpiresAt { get; set; }

    public string? PreviousCredentialReference { get; set; }

    public DateTimeOffset? PreviousValidUntil { get; set; }

    public RecordStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A validated session bound to a device and an authentication strength, so step-up can be
/// proved without trusting a request field. Table: <c>identity.sessions</c>. Decision: ADR-0008.
/// </summary>
public sealed class SessionRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string SessionId { get; set; }

    public required string IdentityId { get; set; }

    public string? DeviceId { get; set; }

    /// <summary>SHA-256 fingerprint of the provider session/family reference; never a token.</summary>
    public required string ProviderSessionFingerprint { get; set; }

    public AuthenticationStrength AuthenticationStrength { get; set; }

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset LastActivityAt { get; set; }

    public CoreBanking.IdentityAccess.Domain.Authentication.SessionLifecycleState Status { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset? ProviderRevocationRequestedAt { get; set; }

    public string? RevocationEventId { get; set; }

    public long ConcurrencyVersion { get; set; }
}
