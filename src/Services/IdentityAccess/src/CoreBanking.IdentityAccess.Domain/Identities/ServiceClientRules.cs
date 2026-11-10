using System;
using System.Collections.Generic;
using System.Linq;

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

    public const int MaximumPermissions = 32;

    public const int MaximumPermissionLength = 160;

    public static IReadOnlyList<string> NormalisePermissions(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        string[] normalised = permissions
            .Select(permission => permission?.Trim() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(permission => permission, StringComparer.Ordinal)
            .ToArray();

        if (normalised.Length > MaximumPermissions)
        {
            throw new ArgumentException($"A service client may have at most {MaximumPermissions} permissions.", nameof(permissions));
        }

        if (normalised.Any(permission => permission.Length == 0
                || permission.Length > MaximumPermissionLength
                || !IsValidServicePermission(permission)))
        {
            throw new ArgumentException(
                "Service permissions must use the service.<domain>.<action> form.",
                nameof(permissions));
        }

        return normalised;
    }

    private static bool IsValidServicePermission(string permission)
    {
        string[] segments = permission.Split('.', StringSplitOptions.None);
        return segments.Length == 3
            && string.Equals(segments[0], "service", StringComparison.Ordinal)
            && segments.All(segment => segment.Length > 0
                && segment.All(character => character is >= 'a' and <= 'z'
                    || character is >= '0' and <= '9'
                    || character == '_'));
    }

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
