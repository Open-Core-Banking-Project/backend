using System;
using CoreBanking.BuildingBlocks.Application.Persistence;
using CoreBanking.IdentityAccess.Domain.Security;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;

/// <summary>
/// One institution key version. It stores public parameters and an opaque KMS/HSM reference; no private key,
/// wrapping key or secret is ever written to a service table. Decision: P1-E02-D13.
/// </summary>
public sealed class SecurityKeyMetadataRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string KeyId { get; set; }

    public int KeyVersion { get; set; }

    public required string Purpose { get; set; }

    public required string Algorithm { get; set; }

    /// <summary>Public parameters in PEM form. Safe to publish to any caller.</summary>
    public required string PublicKey { get; set; }

    /// <summary>Opaque boundary reference. Not a key and useless without the KMS/HSM adapter.</summary>
    public required string ProviderReference { get; set; }

    public required KeyCustodyState State { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset NotBefore { get; set; }

    public DateTimeOffset? NotAfter { get; set; }

    public DateTimeOffset? RetiredAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevocationReason { get; set; }
}

/// <summary>
/// One claimed envelope replay identifier. The unique key is what makes a second use of the same envelope fail;
/// rows live for the bounded replay window only. Decision: P1-E02-D12 item D12-D.
/// </summary>
public sealed class EnvelopeReplayRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string Purpose { get; set; }

    public required string ReplayId { get; set; }

    public DateTimeOffset ClaimedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
