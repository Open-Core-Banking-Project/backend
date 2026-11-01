using System;

namespace CoreBanking.IdentityAccess.Domain.Identities;

/// <summary>
/// Rules for a service client record. A credential is only ever an opaque reference: the provider
/// holds the secret, and the platform stores a pointer with an expiry so rotation can be proven.
/// Decision: ADR-0008, ADR-0011; slice 13.
/// </summary>
public static class ServiceClientRules
{
    public const int MaximumReferenceLength = 200;

    public const int MaximumAudienceLength = 200;

    public static string NormaliseReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A credential reference is required.", nameof(reference));
        }

        string normalised = reference.Trim();
        if (normalised.Length > MaximumReferenceLength)
        {
            throw new ArgumentException($"A credential reference may be at most {MaximumReferenceLength} characters.", nameof(reference));
        }

        return normalised;
    }

    public static string NormaliseAudience(string audience)
    {
        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new ArgumentException("An audience is required.", nameof(audience));
        }

        string normalised = audience.Trim();
        if (normalised.Length > MaximumAudienceLength)
        {
            throw new ArgumentException($"An audience may be at most {MaximumAudienceLength} characters.", nameof(audience));
        }

        return normalised;
    }

    public static string NormaliseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A client name is required.", nameof(name));
        }

        return name.Trim();
    }

    public static void ValidateExpiry(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt is not null && expiresAt <= now)
        {
            throw new ArgumentException("A credential expiry must be in the future.", nameof(expiresAt));
        }
    }

    /// <summary>
    /// A record is usable only while active and unexpired. Missing metadata is a denial, never a
    /// permissive default.
    /// </summary>
    public static bool IsUsable(string status, DateTimeOffset? expiresAt, DateTimeOffset now) =>
        string.Equals(status, "Active", StringComparison.Ordinal)
        && (expiresAt is null || expiresAt > now);
}
