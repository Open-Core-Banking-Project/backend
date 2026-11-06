using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Products;

/// <summary>
/// Resolves the institution's supported currency set from the effective, published
/// <c>institution.currencies</c> configuration version. Missing, overlapping, superseded or corrupt
/// configuration yields no reference, so the caller fails closed. Decision: P1-E04-D17.
/// </summary>
public sealed class CurrencyReferenceSource : ICurrencyReferenceSource
{
    private readonly CoreLedgerDbContext _context;

    public CurrencyReferenceSource(CoreLedgerDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<CurrencyReferenceSnapshot?> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);

        ConfigurationSetRecord? set = await _context.ConfigurationSets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId.Value
                    && record.Area == InstitutionCurrencySet.Area,
                cancellationToken)
            .ConfigureAwait(false);
        if (set is null)
        {
            return null;
        }

        ConfigurationPublicationRecord[] effective = await _context.ConfigurationPublications
            .AsNoTracking()
            .Where(record => record.InstitutionId == institutionId.Value
                && record.ConfigurationSetId == set.ConfigurationSetId
                && record.EffectiveFrom <= asOf
                && (record.EffectiveTo == null || asOf < record.EffectiveTo))
            .Take(2)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (effective.Length != 1)
        {
            return null;
        }

        ConfigurationPublicationRecord publication = effective[0];
        ConfigurationVersionRecord? version = await _context.ConfigurationVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId.Value
                    && record.VersionId == publication.VersionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (version is null
            || !string.Equals(version.Schema, InstitutionCurrencySet.Schema, StringComparison.Ordinal)
            || !string.Equals(version.ConfigurationSetId, set.ConfigurationSetId, StringComparison.Ordinal)
            || !string.Equals(version.Status, "published", StringComparison.Ordinal)
            || !string.Equals(version.ContentHash, publication.ContentHash, StringComparison.Ordinal)
            || version.EffectiveFrom != publication.EffectiveFrom
            || version.EffectiveTo != publication.EffectiveTo)
        {
            return null;
        }

        ConfigurationContent content = ConfigurationContent.Create(version.ContentJson);
        if (!string.Equals(content.Hash, version.ContentHash, StringComparison.Ordinal))
        {
            return null;
        }

        InstitutionCurrencySet currencies;
        try
        {
            currencies = InstitutionCurrencySet.Parse(version.ContentJson);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return new CurrencyReferenceSnapshot(
            institutionId.Value,
            currencies.SupportedCurrencies,
            version.VersionId);
    }
}
