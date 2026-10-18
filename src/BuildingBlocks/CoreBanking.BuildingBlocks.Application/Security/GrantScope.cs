using System;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.BuildingBlocks.Application.Security;

/// <summary>
/// The scope attached to a grant. It must be at least as specific as the request it
/// satisfies: a dimension the request constrains must be constrained identically on the
/// grant, and a monetary limit must be provable from the request.
/// Decision: ADR-0009.
/// </summary>
public sealed record GrantScope
{
    public required InstitutionId InstitutionId { get; init; }

    public string? LegalEntityId { get; init; }

    public string? RegionId { get; init; }

    public string? BranchId { get; init; }

    public string? ChannelId { get; init; }

    public string? CustomerId { get; init; }

    public string? AccountId { get; init; }

    public string? MandateId { get; init; }

    public string? Currency { get; init; }

    /// <summary>Maximum amount this grant authorises. A request guarded by a limit must state an amount.</summary>
    public decimal? MaximumAmount { get; init; }

    public static GrantScope ForInstitution(InstitutionId institutionId) => new() { InstitutionId = institutionId };

    public bool Covers(ResourceScope requested)
    {
        ArgumentNullException.ThrowIfNull(requested);

        if (requested.InstitutionId is null || requested.InstitutionId != InstitutionId)
        {
            return false;
        }

        if (!CoversDimension(LegalEntityId, requested.LegalEntityId)
            || !CoversDimension(RegionId, requested.RegionId)
            || !CoversDimension(BranchId, requested.BranchId)
            || !CoversDimension(ChannelId, requested.ChannelId)
            || !CoversDimension(CustomerId, requested.CustomerId)
            || !CoversDimension(AccountId, requested.AccountId)
            || !CoversDimension(MandateId, requested.MandateId)
            || !CoversDimension(Currency, requested.Currency))
        {
            return false;
        }

        if (MaximumAmount is not null && (requested.Amount is null || requested.Amount > MaximumAmount))
        {
            return false;
        }

        return true;
    }

    private static bool CoversDimension(string? granted, string? requested) =>
        requested is null || string.Equals(granted, requested, StringComparison.Ordinal);
}
