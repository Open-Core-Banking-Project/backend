using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.BuildingBlocks.Application.Security;

/// <summary>
/// The contextual scope a request operates in. Every scope that cannot be resolved is a
/// denial, never a wildcard. Decision: ADR-0009.
/// </summary>
public sealed record ResourceScope
{
    public InstitutionId? InstitutionId { get; init; }

    public string? LegalEntityId { get; init; }

    public string? RegionId { get; init; }

    public string? BranchId { get; init; }

    public string? ChannelId { get; init; }

    public string? CustomerId { get; init; }

    public string? AccountId { get; init; }

    public string? MandateId { get; init; }

    public string? Currency { get; init; }

    public decimal? Amount { get; init; }

    public static ResourceScope None { get; } = new();

    public static ResourceScope ForInstitution(InstitutionId institutionId) => new() { InstitutionId = institutionId };
}
