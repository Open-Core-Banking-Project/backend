using System;
using System.Security.Cryptography;
using System.Text;
using CoreBanking.CustomerOnboarding.Application.Onboarding;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Protection;

/// <summary>
/// AES-256-GCM for the stored value and HMAC-SHA256 for exact-match lookup. The value token is
/// randomised, so two captures of the same value do not reveal the match by themselves; only the
/// lookup token is deterministic. Neither token can be produced without the key material, and the
/// plaintext is never part of an exception message. Decision: CUS-028.
/// </summary>
public sealed class AesGcmProtectedValueProtector : IProtectedValueProtector
{
    private const string TokenPrefix = "v1.";
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int VisibleTailCharacters = 4;
    private const int MinimumMaskedLength = 3;

    private readonly byte[] _encryptionKey;
    private readonly byte[] _lookupKey;

    public AesGcmProtectedValueProtector(ProtectedValueProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _encryptionKey = options.EncryptionKey;
        _lookupKey = options.LookupKey;
    }

    public ProtectedValue Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

        string value = plaintext.Trim();
        byte[] valueBytes = Encoding.UTF8.GetBytes(value);
        byte[] lookupInput = Encoding.UTF8.GetBytes("v1:" + value);

        try
        {
            string lookupToken = Convert.ToHexStringLower(HMACSHA256.HashData(_lookupKey, lookupInput));

            byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
            byte[] ciphertext = new byte[valueBytes.Length];
            byte[] tag = new byte[TagBytes];

            using (AesGcm aes = new(_encryptionKey, TagBytes))
            {
                aes.Encrypt(nonce, valueBytes, ciphertext, tag);
            }

            byte[] token = new byte[NonceBytes + TagBytes + ciphertext.Length];
            nonce.CopyTo(token, 0);
            tag.CopyTo(token, NonceBytes);
            ciphertext.CopyTo(token, NonceBytes + TagBytes);

            return new ProtectedValue(TokenPrefix + Convert.ToBase64String(token), lookupToken, Mask(value));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(valueBytes);
            CryptographicOperations.ZeroMemory(lookupInput);
        }
    }

    /// <summary>Masked display form: the tail is kept so staff can confirm a value they already hold.</summary>
    internal static string Mask(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        int visible = value.Length > VisibleTailCharacters * 2 ? VisibleTailCharacters : 0;
        string tail = visible == 0 ? string.Empty : value[^visible..];
        int hidden = Math.Max(MinimumMaskedLength, value.Length - visible);

        return new string('*', hidden) + tail;
    }
}
