using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

/// <summary>One effective-dated preference version. Only one version per customer/purpose/channel is current.</summary>
public sealed class ServicePreferenceRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string PreferenceId { get; set; } = string.Empty;

    public string CustomerId { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;

    public int Version { get; set; }

    public string State { get; set; } = string.Empty;

    public string? LawfulBasis { get; set; }

    public string Source { get; set; } = string.Empty;

    public string? EvidenceReference { get; set; }

    public string? Reason { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }

    public DateTimeOffset? EffectiveTo { get; set; }

    public string RecordedBy { get; set; } = string.Empty;
}

/// <summary>Append-only consent evidence for one preference version. Decision: P1-E03-D12.</summary>
public sealed class ConsentEvidenceRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string ConsentId { get; set; } = string.Empty;

    public string CustomerId { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;

    public int Version { get; set; }

    public string State { get; set; } = string.Empty;

    public string? LawfulBasis { get; set; }

    public string Source { get; set; } = string.Empty;

    public string? EvidenceReference { get; set; }

    public string RecordedBy { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }

    public DateTimeOffset? WithdrawnAt { get; set; }
}
