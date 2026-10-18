using System;
using System.Collections.Generic;

namespace CoreBanking.BuildingBlocks.Application.Security;

/// <summary>
/// A stable capability permission identifier in <c>domain.resource.action</c> form.
/// Decision: docs/adr/ADR-0009-roles-permissions-contextual-scopes-and-invalidation.md
/// </summary>
/// <remarks>
/// Identifiers are permanent. A replacement permission is added and the old one retired;
/// an identifier is never renamed.
///
/// The type is named <c>PermissionIdentifier</c> rather than <c>Permission</c> because the
/// SDK naming rule CA1711 reserves the <c>Permission</c> suffix, and ADR-0003 forbids
/// suppressing an analyzer rule without an approved work item.
/// </remarks>
public sealed record PermissionIdentifier
{
    private const int SegmentCount = 3;
    private static readonly char[] SegmentSeparator = { '.' };

    private PermissionIdentifier(string value, string domain, string resource, string action)
    {
        Value = value;
        Domain = domain;
        Resource = resource;
        Action = action;
    }

    public string Value { get; }

    public string Domain { get; }

    public string Resource { get; }

    public string Action { get; }

    public static PermissionIdentifier Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A permission value is required.", nameof(value));
        }

        string[] segments = value.Trim().Split(SegmentSeparator, StringSplitOptions.None);
        if (segments.Length != SegmentCount)
        {
            throw new ArgumentException($"A permission must have exactly {SegmentCount} segments in domain.resource.action form.", nameof(value));
        }

        foreach (string segment in segments)
        {
            EnsureSegment(segment, value);
        }

        return new PermissionIdentifier(value.Trim(), segments[0], segments[1], segments[2]);
    }

    public override string ToString() => Value;

    private static void EnsureSegment(string segment, string value)
    {
        if (segment.Length == 0 || !char.IsAsciiLetterLower(segment[0]))
        {
            throw new ArgumentException($"Permission segment '{segment}' in '{value}' must start with a lower-case letter.", nameof(value));
        }

        foreach (char character in segment)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '_')
            {
                throw new ArgumentException($"Permission segment '{segment}' in '{value}' may contain only lower-case letters, digits and underscores.", nameof(value));
            }
        }
    }

    /// <summary>Actions recognised by the permission naming convention.</summary>
    public static IReadOnlySet<string> ApprovedActions { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "view",
        "create",
        "update",
        "delete",
        "approve",
        "reject",
        "execute",
        "manage",
        "override",
        "initiate",
        "review",
        "assign",
    };
}
