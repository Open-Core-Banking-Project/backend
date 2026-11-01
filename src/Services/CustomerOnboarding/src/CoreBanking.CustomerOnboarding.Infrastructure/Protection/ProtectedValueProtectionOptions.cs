using System;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Protection;

/// <summary>
/// Key material for <see cref="AesGcmProtectedValueProtector"/>. Both keys are base64 and must be
/// distinct 256-bit secrets supplied by the deployment secret store; they are never defaulted, so a
/// missing key fails at startup instead of storing weakly protected data. Decision: CUS-028.
/// </summary>
public sealed record ProtectedValueProtectionOptions
{
    private const int RequiredKeyBytes = 32;

    private ProtectedValueProtectionOptions(byte[] encryptionKey, byte[] lookupKey)
    {
        EncryptionKey = encryptionKey;
        LookupKey = lookupKey;
    }

    public byte[] EncryptionKey { get; }

    public byte[] LookupKey { get; }

    /// <summary>Reads the configuration keys <c>Privacy:Protection:EncryptionKey</c> and <c>Privacy:Protection:LookupKey</c>.</summary>
    public static ProtectedValueProtectionOptions Create(string? encryptionKeyBase64, string? lookupKeyBase64)
    {
        byte[] encryptionKey = Decode(encryptionKeyBase64, "Privacy:Protection:EncryptionKey");
        byte[] lookupKey = Decode(lookupKeyBase64, "Privacy:Protection:LookupKey");

        if (encryptionKey.AsSpan().SequenceEqual(lookupKey))
        {
            throw new InvalidOperationException(
                "Privacy:Protection:EncryptionKey and Privacy:Protection:LookupKey must be different secrets.");
        }

        return new ProtectedValueProtectionOptions(encryptionKey, lookupKey);
    }

    private static byte[] Decode(string? value, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{settingName} is required to protect customer data.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"{settingName} must be base64-encoded key material.", exception);
        }

        if (key.Length != RequiredKeyBytes)
        {
            throw new InvalidOperationException($"{settingName} must decode to {RequiredKeyBytes} bytes.");
        }

        return key;
    }
}
