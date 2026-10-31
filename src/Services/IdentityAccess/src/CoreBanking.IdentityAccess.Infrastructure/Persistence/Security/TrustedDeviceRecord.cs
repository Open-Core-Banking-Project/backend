using System;
using CoreBanking.BuildingBlocks.Application.Persistence;
using CoreBanking.IdentityAccess.Domain.Authentication;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;

/// <summary>
/// One trusted-device registration. Only a public-key thumbprint is stored, never a key or a secret, and trust
/// is effective-dated. Decision: P1-E02-D11.
/// </summary>
public sealed class TrustedDeviceRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string DeviceId { get; set; }

    public required string IdentityId { get; set; }

    /// <summary>Base64url SHA-256 of the exported public key.</summary>
    public required string KeyThumbprint { get; set; }

    public required string Platform { get; set; }

    /// <summary>Untrusted user label; trimmed and bounded, never used for authentication.</summary>
    public string? DisplayName { get; set; }

    public required DeviceTrustState TrustState { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? TrustedAt { get; set; }

    public DateTimeOffset? TrustExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevocationReason { get; set; }

    public string? StepUpEvidenceId { get; set; }

    public long ConcurrencyVersion { get; set; }
}
