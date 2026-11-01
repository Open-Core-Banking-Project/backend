using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreBanking.IdentityAccess.Domain.Security;

/// <summary>Lifecycle of one key version. Retirement keeps unwrapping possible for a bounded overlap.</summary>
public enum KeyCustodyState
{
    Active,
    Retired,
    Revoked,
}

/// <summary>Whether a key version may unwrap a content key at a point in time.</summary>
public enum KeyUsability
{
    Usable,
    NotYetValid,
    Expired,
    RetiredBeyondOverlap,
    Revoked,
}

/// <summary>The key purposes this assembly records. One purpose, one wrapping key.</summary>
public static class KeyPurposes
{
    public const string PayloadWrapping = "payload.wrapping";
}

/// <summary>
/// Public metadata and the opaque provider reference for one institution key version. It never holds key
/// material: the private key stays in the KMS/HSM boundary and only <see cref="ProviderReference"/> crosses into
/// the adapter. Decisions: P1-E02-D12, P1-E02-D13.
/// </summary>
public sealed class SecurityKeyMetadata
{
    public const int MaximumKeyIdLength = 64;
    public const int MaximumProviderReferenceLength = 256;
    public const int MaximumPublicKeyLength = 4096;
    public const int MaximumReasonLength = 500;

    /// <summary>Only approved suites may be described or used. Decision: P1-E02-D12 item D12-B.</summary>
    public static IReadOnlyList<string> ApprovedAlgorithms { get; } = ["RSA-OAEP-256+A256GCM"];

    private SecurityKeyMetadata(
        string institutionId,
        string keyId,
        int version,
        string purpose,
        string algorithm,
        string publicKey,
        string providerReference,
        KeyCustodyState state,
        DateTimeOffset createdAt,
        DateTimeOffset notBefore,
        DateTimeOffset? notAfter,
        DateTimeOffset? retiredAt,
        DateTimeOffset? revokedAt,
        string? revocationReason)
    {
        InstitutionId = institutionId;
        KeyId = keyId;
        Version = version;
        Purpose = purpose;
        Algorithm = algorithm;
        PublicKey = publicKey;
        ProviderReference = providerReference;
        State = state;
        CreatedAt = createdAt;
        NotBefore = notBefore;
        NotAfter = notAfter;
        RetiredAt = retiredAt;
        RevokedAt = revokedAt;
        RevocationReason = revocationReason;
    }

    /// <summary>Trusted tenant identity; the domain stays dependency-free, like the rest of this assembly.</summary>
    public string InstitutionId { get; }

    public string KeyId { get; }

    public int Version { get; }

    public string Purpose { get; }

    public string Algorithm { get; }

    /// <summary>Public parameters only, in PEM form. There is no private member to leak.</summary>
    public string PublicKey { get; }

    /// <summary>Opaque KMS/HSM reference. It is not a key and confers nothing without the boundary.</summary>
    public string ProviderReference { get; }

    public KeyCustodyState State { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset NotBefore { get; }

    public DateTimeOffset? NotAfter { get; private set; }

    public DateTimeOffset? RetiredAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevocationReason { get; private set; }

    public static SecurityKeyMetadata Register(
        string institutionId,
        string keyId,
        int version,
        string purpose,
        string algorithm,
        string publicKey,
        string providerReference,
        DateTimeOffset now,
        DateTimeOffset? notAfter = null)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerReference);

        if (keyId.Trim().Length > MaximumKeyIdLength)
        {
            throw new ArgumentException($"A key identifier may hold at most {MaximumKeyIdLength} characters.", nameof(keyId));
        }

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "A key version starts at one.");
        }

        if (providerReference.Trim().Length > MaximumProviderReferenceLength)
        {
            throw new ArgumentException(
                $"A provider reference may hold at most {MaximumProviderReferenceLength} characters.",
                nameof(providerReference));
        }

        if (publicKey.Length > MaximumPublicKeyLength)
        {
            throw new ArgumentException($"A public key may hold at most {MaximumPublicKeyLength} characters.", nameof(publicKey));
        }

        if (notAfter is not null && notAfter <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(notAfter), "A key must remain valid for a positive period.");
        }

        return new SecurityKeyMetadata(
            institutionId,
            keyId.Trim(),
            version,
            purpose.Trim(),
            Approved(algorithm),
            publicKey.Trim(),
            providerReference.Trim(),
            KeyCustodyState.Active,
            now,
            now,
            notAfter,
            retiredAt: null,
            revokedAt: null,
            revocationReason: null);
    }

    public static SecurityKeyMetadata Restore(
        string institutionId,
        string keyId,
        int version,
        string purpose,
        string algorithm,
        string publicKey,
        string providerReference,
        KeyCustodyState state,
        DateTimeOffset createdAt,
        DateTimeOffset notBefore,
        DateTimeOffset? notAfter,
        DateTimeOffset? retiredAt,
        DateTimeOffset? revokedAt,
        string? revocationReason) =>
        new(
            institutionId,
            keyId,
            version,
            purpose,
            algorithm,
            publicKey,
            providerReference,
            state,
            createdAt,
            notBefore,
            notAfter,
            retiredAt,
            revokedAt,
            revocationReason);

    /// <summary>
    /// Effective usability. A revoked version never unwraps; an expired window or a retirement whose overlap has
    /// elapsed behaves expired. Decision: P1-E02-D13 item D13-F.
    /// </summary>
    public KeyUsability Evaluate(DateTimeOffset now, TimeSpan retirementOverlap)
    {
        if (State == KeyCustodyState.Revoked)
        {
            return KeyUsability.Revoked;
        }

        if (now < NotBefore)
        {
            return KeyUsability.NotYetValid;
        }

        if (NotAfter is not null && now >= NotAfter.Value)
        {
            return KeyUsability.Expired;
        }

        if (State == KeyCustodyState.Retired
            && RetiredAt is not null
            && now >= RetiredAt.Value.Add(retirementOverlap))
        {
            return KeyUsability.RetiredBeyondOverlap;
        }

        return KeyUsability.Usable;
    }

    public bool IsAdvertisedAt(DateTimeOffset now) =>
        State == KeyCustodyState.Active
        && now >= NotBefore
        && (NotAfter is null || now < NotAfter.Value);

    public void Retire(DateTimeOffset now)
    {
        if (State == KeyCustodyState.Revoked)
        {
            throw new InvalidOperationException("A revoked key version cannot be retired.");
        }

        State = KeyCustodyState.Retired;
        RetiredAt ??= now;
    }

    public void Revoke(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        string trimmed = reason.Trim();
        if (trimmed.Length > MaximumReasonLength)
        {
            throw new ArgumentException($"A revocation reason may hold at most {MaximumReasonLength} characters.", nameof(reason));
        }

        State = KeyCustodyState.Revoked;
        RevokedAt ??= now;
        RevocationReason ??= trimmed;
    }

    private static string Approved(string algorithm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        string trimmed = algorithm.Trim();
        if (!ApprovedAlgorithms.Contains(trimmed, StringComparer.Ordinal))
        {
            throw new ArgumentException("The algorithm is not approved for payload envelopes.", nameof(algorithm));
        }

        return trimmed;
    }
}
