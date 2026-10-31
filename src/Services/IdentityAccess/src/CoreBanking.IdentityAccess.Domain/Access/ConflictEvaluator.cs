using System;
using System.Collections.Generic;

namespace CoreBanking.IdentityAccess.Domain.Access;

/// <summary>One prohibited pair. Either order is a conflict, and comparison is case-sensitive.</summary>
public sealed record GrantConflict(string PermissionCode, string ConflictingPermissionCode);

/// <summary>
/// Evaluates segregation-of-duties rules before a grant becomes active. Rules are institution data,
/// never hard-coded role names, and an empty rule set never silently permits a wildcard.
/// Decision: ADR-0009 and P1-E02-D10.
/// </summary>
public static class ConflictEvaluator
{
    public static GrantConflict? FindFirst(IReadOnlySet<string> permissions, IEnumerable<GrantConflict> rules)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(rules);

        foreach (GrantConflict rule in rules)
        {
            if (permissions.Contains(rule.PermissionCode) && permissions.Contains(rule.ConflictingPermissionCode))
            {
                return rule;
            }
        }

        return null;
    }
}
