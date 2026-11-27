using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Ledger;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Ledger;

/// <summary>
/// Resolves the effective, published <c>ledger.manual_journal_policy</c> version. A missing, overlapping, superseded
/// or corrupt policy yields no policy, so a manual journal is refused rather than defaulted.
/// Decision: P1-E05-D11.
/// </summary>
public sealed class ManualJournalPolicySource : IManualJournalPolicySource
{
    private readonly CoreLedgerDbContext _context;

    public ManualJournalPolicySource(CoreLedgerDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<ManualJournalPolicyLookup> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);

        ConfigurationSetRecord? set = await _context.ConfigurationSets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId.Value && record.Area == ManualJournalPolicy.Area,
                cancellationToken)
            .ConfigureAwait(false);
        if (set is null)
        {
            return ManualJournalPolicyLookup.Rejected("manual_journal_policy_missing");
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
        if (effective.Length == 0)
        {
            return ManualJournalPolicyLookup.Rejected("manual_journal_policy_missing");
        }

        if (effective.Length > 1)
        {
            return ManualJournalPolicyLookup.Rejected("manual_journal_policy_ambiguous");
        }

        ConfigurationPublicationRecord publication = effective[0];
        ConfigurationVersionRecord? version = await _context.ConfigurationVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId.Value && record.VersionId == publication.VersionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (version is null
            || !string.Equals(version.Schema, ManualJournalPolicy.Schema, StringComparison.Ordinal)
            || !string.Equals(version.ConfigurationSetId, set.ConfigurationSetId, StringComparison.Ordinal)
            || !string.Equals(version.Status, "published", StringComparison.Ordinal)
            || !string.Equals(version.ContentHash, publication.ContentHash, StringComparison.Ordinal)
            || version.EffectiveFrom != publication.EffectiveFrom
            || version.EffectiveTo != publication.EffectiveTo)
        {
            return ManualJournalPolicyLookup.Rejected("manual_journal_policy_integrity_failed");
        }

        ConfigurationContent content = ConfigurationContent.Create(version.ContentJson);
        if (!string.Equals(content.Hash, version.ContentHash, StringComparison.Ordinal))
        {
            return ManualJournalPolicyLookup.Rejected("manual_journal_policy_integrity_failed");
        }

        try
        {
            return ManualJournalPolicyLookup.FoundPolicy(
                version.VersionId,
                version.ContentHash,
                ManualJournalPolicy.Parse(version.ContentJson));
        }
        catch (ArgumentException)
        {
            return ManualJournalPolicyLookup.Rejected("manual_journal_policy_invalid");
        }
    }
}
