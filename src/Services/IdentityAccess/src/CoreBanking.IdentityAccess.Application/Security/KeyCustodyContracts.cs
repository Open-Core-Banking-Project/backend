using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.IdentityAccess.Application.Security;

/// <summary>Public parameters reported by the KMS/HSM boundary. There is no private member by design.</summary>
public sealed record KeyDescription(string Algorithm, string PublicKey, DateTimeOffset CreatedAt);

/// <summary>One unwrap request. The adapter receives no institution, identity, purpose or plaintext.</summary>
public sealed record KeyOperationRequest(
    string ProviderReference,
    int KeyVersion,
    string Algorithm,
    byte[] WrappedKey);

/// <summary>
/// The only way private-key material is used. Two operations, no payload decryption: the boundary unwraps a
/// bounded content key and the service performs the field decryption outside the hardware boundary.
/// Decision: P1-E02-D13 items D13-B to D13-E.
/// </summary>
public interface IKeyOperationPort
{
    /// <summary>Reads public parameters for one key reference. Never returns private material.</summary>
    Task<KeyDescription> DescribeAsync(string providerReference, CancellationToken cancellationToken);

    /// <summary>Unwraps one content key. Failures must surface as <see cref="KeyOperationException"/>.</summary>
    Task<byte[]> UnwrapAsync(KeyOperationRequest request, CancellationToken cancellationToken);
}

/// <summary>A boundary failure. The code is always the stable <c>payload_decryption_unavailable</c>.</summary>
public sealed class KeyOperationException : Exception
{
    public KeyOperationException(string message)
        : base(message) => Code = PayloadEnvelopeCodes.DecryptionUnavailable;

    public KeyOperationException(string message, Exception innerException)
        : base(message, innerException) => Code = PayloadEnvelopeCodes.DecryptionUnavailable;

    public string Code { get; }
}

/// <summary>One advertised key version. It carries public parameters only.</summary>
public sealed record PublicKeyView(
    string KeyId,
    int KeyVersion,
    string Purpose,
    string Algorithm,
    string PublicKey,
    DateTimeOffset NotBefore,
    DateTimeOffset? NotAfter);

/// <summary>The discovery response bounded to one purpose.</summary>
public sealed record PublicKeySetView(string Purpose, IReadOnlyList<PublicKeyView> Keys);

/// <summary>
/// Registers and exposes institution key metadata. Discovery answers with active versions only; rotation moves a
/// version to retired and then revoked. Decision: P1-E02-D13 item D13-F.
/// </summary>
public interface IPublicKeyMetadataService
{
    /// <summary>Advertises the active, currently valid key versions for one purpose.</summary>
    Task<PublicKeySetView> GetActiveKeysAsync(
        InstitutionId institutionId,
        string purpose,
        CancellationToken cancellationToken);

    /// <summary>
    /// Registers the next version for a purpose from the boundary's public parameters. Platform operations own
    /// key creation; this records the metadata the boundary already holds.
    /// </summary>
    Task<PublicKeyView> RegisterVersionAsync(
        InstitutionId institutionId,
        string keyId,
        string purpose,
        string providerReference,
        CancellationToken cancellationToken);

    /// <summary>Stops advertising a version while it may still unwrap in-flight envelopes.</summary>
    Task<PublicKeyView> RetireAsync(InstitutionId institutionId, string keyId, int keyVersion, CancellationToken cancellationToken);

    /// <summary>Refuses a version immediately, with a recorded reason.</summary>
    Task<PublicKeyView> RevokeAsync(
        InstitutionId institutionId,
        string keyId,
        int keyVersion,
        string reason,
        CancellationToken cancellationToken);
}
