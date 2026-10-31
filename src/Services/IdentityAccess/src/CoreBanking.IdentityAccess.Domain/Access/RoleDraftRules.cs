using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreBanking.IdentityAccess.Domain.Access;

/// <summary>
/// Rules for drafting a role version. A draft is inert: it cannot be granted until a later
/// publication slice activates it through the approved approval path.
/// Decision: ADR-0009, P1-E02-D09 and P1-E02-D10.
/// </summary>
public static class RoleDraftRules
{
    public const int MaximumCodeLength = 64;

    public const int MaximumNameLength = 200;

    /// <summary>Normalises a role code and throws when it cannot be a stable identifier.</summary>
    public static string NormaliseCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("A role code is required.", nameof(code));
        }

        string normalised = code.Trim().ToLowerInvariant();
        if (normalised.Length is < 2 or > MaximumCodeLength)
        {
            throw new ArgumentException($"A role code must be 2 to {MaximumCodeLength} characters.", nameof(code));
        }

        if (!char.IsAsciiLetterLower(normalised[0]))
        {
            throw new ArgumentException("A role code must start with a lower-case letter.", nameof(code));
        }

        foreach (char character in normalised)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character is not ('.' or '_' or '-'))
            {
                throw new ArgumentException("A role code may contain only lower-case letters, digits, dot, underscore and hyphen.", nameof(code));
            }
        }

        return normalised;
    }

    public static string NormaliseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A role name is required.", nameof(name));
        }

        string normalised = name.Trim();
        if (normalised.Length > MaximumNameLength)
        {
            throw new ArgumentException($"A role name may be at most {MaximumNameLength} characters.", nameof(name));
        }

        return normalised;
    }

    /// <summary>Deduplicates and orders the requested permissions so a draft is deterministic.</summary>
    public static IReadOnlyList<string> NormalisePermissions(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return permissions
            .Select(permission => permission?.Trim() ?? string.Empty)
            .Where(permission => permission.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Returns the requested codes the platform catalogue does not contain.</summary>
    public static IReadOnlyList<string> UnknownPermissions(IEnumerable<string> permissions, IReadOnlySet<string> catalogue)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(catalogue);

        return permissions
            .Where(permission => !catalogue.Contains(permission))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Returns every prohibited pair contained in one permission set. A role that conflicts with
    /// itself can never be published, so the draft surface refuses it before approval work starts.
    /// </summary>
    public static IReadOnlyList<GrantConflict> SelfConflicts(IEnumerable<string> permissions, IEnumerable<GrantConflict> rules)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(rules);

        HashSet<string> set = new(permissions, StringComparer.Ordinal);
        List<GrantConflict> conflicts = [];
        foreach (GrantConflict rule in rules)
        {
            if (set.Contains(rule.PermissionCode) && set.Contains(rule.ConflictingPermissionCode))
            {
                conflicts.Add(rule);
            }
        }

        return conflicts;
    }
}
