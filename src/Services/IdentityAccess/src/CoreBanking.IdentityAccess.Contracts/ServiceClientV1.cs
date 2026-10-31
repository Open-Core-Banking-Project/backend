using System;

namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>Stable values for service client administration. Decision: ADR-0008; slice 13.</summary>
public static class ServiceClientContractV1
{
    public const int SchemaVersion = IdentityAccessContractV1.SchemaVersion;

    public const string StatusActive = "active";
    public const string StatusRevoked = "revoked";

    public const string InvalidRequest = "request.invalid_service_client";
    public const string NotFound = "identity.service_client_not_found";
    public const string AlreadyRevoked = "identity.service_client_revoked";
    public const string InvalidServiceIdentity = "auth.service_identity_not_valid";
}

public sealed record ServiceClientRegistrationRequestV1
{
    public int SchemaVersion { get; init; } = ServiceClientContractV1.SchemaVersion;

    public required string ClientName { get; init; }

    /// <summary>The audience this client may request tokens for.</summary>
    public required string Audience { get; init; }

    /// <summary>An opaque provider reference. Never a secret value.</summary>
    public required string CredentialReference { get; init; }

    public DateTimeOffset? CredentialExpiresAt { get; init; }
}

public sealed record ServiceClientRotationRequestV1
{
    public int SchemaVersion { get; init; } = ServiceClientContractV1.SchemaVersion;

    public required string CredentialReference { get; init; }

    public required DateTimeOffset CredentialExpiresAt { get; init; }

    /// <summary>Until when the previous reference remains valid at the provider.</summary>
    public DateTimeOffset? PreviousValidUntil { get; init; }
}

public sealed record ServiceClientRevocationRequestV1
{
    public int SchemaVersion { get; init; } = ServiceClientContractV1.SchemaVersion;

    public required string Reason { get; init; }
}

/// <summary>Data-minimised view. The credential reference is not returned.</summary>
public sealed record ServiceClientResultV1
{
    public int SchemaVersion { get; init; } = ServiceClientContractV1.SchemaVersion;

    public required string ServiceClientId { get; init; }

    public required string ClientName { get; init; }

    public required string Audience { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset? CredentialExpiresAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
