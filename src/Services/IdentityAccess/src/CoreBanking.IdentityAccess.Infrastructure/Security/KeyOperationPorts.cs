using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.IdentityAccess.Application.Security;

namespace CoreBanking.IdentityAccess.Infrastructure.Security;

/// <summary>
/// The fail-closed adapter used whenever no KMS/HSM provider is configured. Every operation refuses, so a
/// deployment can never fall back to a local key or plaintext by accident.
/// Decision: P1-E02-D13 items D13-D and D13-E.
/// </summary>
public sealed class UnconfiguredKeyOperationPort : IKeyOperationPort
{
    public Task<KeyDescription> DescribeAsync(string providerReference, CancellationToken cancellationToken) =>
        throw new KeyOperationException("No key-management boundary is configured.");

    public Task<byte[]> UnwrapAsync(KeyOperationRequest request, CancellationToken cancellationToken) =>
        throw new KeyOperationException("No key-management boundary is configured.");
}

/// <summary>
/// The development adapter. It performs the same RSA-OAEP-SHA256 unwrap the production boundary will perform, with
/// a synthetic wrapping key read through the shared secret provider by logical name. It is a local stand-in only:
/// the key is non-production, it is never stored in a service table and a missing key refuses every operation.
/// Decision: P1-E02-D13 item D13-E.
/// </summary>
public sealed class DevelopmentKeyOperationPort : IKeyOperationPort
{
    public const string DefaultProviderReference = "identity-payload-wrapping-development";

    private const int MinimumModulusBits = 2048;

    private readonly ISecretProvider _secrets;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();

    private RSA? _cachedKey;
    private string? _cachedReference;
    private string? _cachedPublicKey;
    private DateTimeOffset _cachedCreatedAt;

    public DevelopmentKeyOperationPort(ISecretProvider secrets, TimeProvider timeProvider)
    {
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<KeyDescription> DescribeAsync(string providerReference, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerReference);

        RSA key = await LoadAsync(providerReference, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            return new KeyDescription(PayloadEnvelopePolicy.Algorithm, _cachedPublicKey!, _cachedCreatedAt);
        }
    }

    public async Task<byte[]> UnwrapAsync(KeyOperationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        RSA key = await LoadAsync(request.ProviderReference, cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                return key.Decrypt(request.WrappedKey, RSAEncryptionPadding.OaepSHA256);
            }
        }
        catch (CryptographicException exception)
        {
            throw new KeyOperationException("The wrapped content key could not be opened.", exception);
        }
    }

    private async Task<RSA> LoadAsync(string providerReference, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_cachedKey is not null && string.Equals(_cachedReference, providerReference, StringComparison.Ordinal))
            {
                return _cachedKey;
            }
        }

        string? pem = await _secrets.TryReadAsync(providerReference, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new KeyOperationException("No development wrapping key is configured.");
        }

        RSA key = RSA.Create();
        try
        {
            key.ImportFromPem(pem);
            if (key.KeySize < MinimumModulusBits)
            {
                throw new KeyOperationException($"A wrapping key must be at least {MinimumModulusBits} bits.");
            }

            string publicKey = key.ExportSubjectPublicKeyInfoPem();
            lock (_gate)
            {
                _cachedKey = key;
                _cachedReference = providerReference;
                _cachedPublicKey = publicKey;
                _cachedCreatedAt = _timeProvider.GetUtcNow();
                return key;
            }
        }
        catch (ArgumentException exception)
        {
            key.Dispose();
            throw new KeyOperationException("The development wrapping key is not a usable private key.", exception);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }
}
