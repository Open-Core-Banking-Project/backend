using System;
using CoreBanking.BuildingBlocks.Application.Persistence;
using CoreBanking.BuildingBlocks.Application.Security;

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

    public required string DisplayName { get; set; }

    public RecordStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
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

    public AuthenticationStrength AuthenticationStrength { get; set; }

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
