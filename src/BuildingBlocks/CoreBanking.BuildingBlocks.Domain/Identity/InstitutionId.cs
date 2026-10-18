using System;

namespace CoreBanking.BuildingBlocks.Domain.Identity;

/// <summary>
/// Strongly typed identifier for an institution, the tenant boundary of every owned record.
/// Decision: docs/adr/ADR-0004-database-per-service-and-tenant-isolation.md
/// </summary>
public sealed record InstitutionId
{
    private InstitutionId(string value) => Value = value;

    public string Value { get; }

    public static InstitutionId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An institution identifier is required.", nameof(value));
        }

        return new InstitutionId(value.Trim());
    }

    public override string ToString() => Value;
}
