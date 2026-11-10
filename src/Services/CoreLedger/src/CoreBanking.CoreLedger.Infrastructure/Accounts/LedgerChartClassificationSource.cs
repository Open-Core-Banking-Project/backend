using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>Resolves the exact effective published chart; missing or ambiguous evidence fails closed.</summary>
public sealed class LedgerChartClassificationSource : ILedgerChartClassificationSource
{
    private readonly CoreLedgerDbContext _context;

    public LedgerChartClassificationSource(CoreLedgerDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<LedgerChartClassificationLookup> ResolveAsync(
        InstitutionId institutionId,
        string currencyCode,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);

        var candidates = await (
            from set in _context.ConfigurationSets.AsNoTracking()
            join publication in _context.ConfigurationPublications.AsNoTracking()
                on set.ConfigurationSetId equals publication.ConfigurationSetId
            join version in _context.ConfigurationVersions.AsNoTracking()
                on publication.VersionId equals version.VersionId
            where set.InstitutionId == institutionId.Value
                && set.Area == LedgerConfigurationContent.ChartArea
                && publication.EffectiveFrom <= asOf
                && (publication.EffectiveTo == null || asOf < publication.EffectiveTo)
                && version.Schema == LedgerConfigurationContent.ChartSchema
                && version.Status == "published"
            select new
            {
                version.VersionId,
                version.ContentJson,
                VersionHash = version.ContentHash,
                PublicationHash = publication.ContentHash,
            })
            .Take(2)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Length == 0)
        {
            return LedgerChartClassificationLookup.Rejected("chart_version_not_effective");
        }

        if (candidates.Length > 1)
        {
            return LedgerChartClassificationLookup.Rejected("chart_version_ambiguous");
        }

        var candidate = candidates[0];
        if (!string.Equals(candidate.VersionHash, candidate.PublicationHash, StringComparison.Ordinal))
        {
            return LedgerChartClassificationLookup.Rejected("chart_content_integrity_failed");
        }

        try
        {
            var content = LedgerConfigurationContent.ValidateChart(candidate.ContentJson);
            if (!string.Equals(candidate.VersionHash, content.Hash, StringComparison.Ordinal))
            {
                return LedgerChartClassificationLookup.Rejected("chart_content_integrity_failed");
            }

            LedgerAccountClassification classification = LedgerConfigurationContent.ResolveCustomerDeposit(
                content.CanonicalJson,
                currencyCode);
            return LedgerChartClassificationLookup.Found(
                candidate.VersionId,
                classification.LegalEntityCode,
                classification.GlAccountCode);
        }
        catch (LedgerMappingException exception)
        {
            return LedgerChartClassificationLookup.Rejected(exception.Code);
        }
        catch (ArgumentException)
        {
            return LedgerChartClassificationLookup.Rejected("chart_content_integrity_failed");
        }
    }
}

