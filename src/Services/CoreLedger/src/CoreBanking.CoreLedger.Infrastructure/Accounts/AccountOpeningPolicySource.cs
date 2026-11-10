using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Resolves the institution opening ceiling from the effective, published
/// <c>institution.account-opening-policy</c> version. A missing, overlapping, superseded or corrupt
/// version yields no policy, and the caller fails closed rather than assuming "unlimited".
/// Decision: P1-E04-D07.
/// </summary>
public sealed class AccountOpeningPolicySource : IAccountOpeningPolicySource
{
    private readonly CoreLedgerDbContext _context;

    public AccountOpeningPolicySource(CoreLedgerDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<AccountOpeningPolicyLookup> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);

        ConfigurationSetRecord? set = await _context.ConfigurationSets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId.Value
                    && record.Area == AccountOpeningPolicy.Area,
                cancellationToken)
            .ConfigureAwait(false);
        if (set is null)
        {
            return AccountOpeningPolicyLookup.Rejected("account_opening_policy_unavailable");
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
            return AccountOpeningPolicyLookup.Rejected("account_opening_policy_unavailable");
        }

        if (effective.Length > 1)
        {
            return AccountOpeningPolicyLookup.Rejected("account_opening_policy_ambiguous");
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
            || !string.Equals(version.Schema, AccountOpeningPolicy.Schema, StringComparison.Ordinal)
            || !string.Equals(version.ConfigurationSetId, set.ConfigurationSetId, StringComparison.Ordinal)
            || !string.Equals(version.Status, "published", StringComparison.Ordinal)
            || !string.Equals(version.ContentHash, publication.ContentHash, StringComparison.Ordinal)
            || version.EffectiveFrom != publication.EffectiveFrom
            || version.EffectiveTo != publication.EffectiveTo)
        {
            return AccountOpeningPolicyLookup.Rejected("account_opening_policy_integrity_failed");
        }

        ConfigurationContent content = ConfigurationContent.Create(version.ContentJson);
        if (!string.Equals(content.Hash, version.ContentHash, StringComparison.Ordinal))
        {
            return AccountOpeningPolicyLookup.Rejected("account_opening_policy_integrity_failed");
        }

        try
        {
            return AccountOpeningPolicyLookup.Found(
                AccountOpeningPolicy.Parse(version.ContentJson),
                version.VersionId);
        }
        catch (ArgumentException)
        {
            return AccountOpeningPolicyLookup.Rejected("account_opening_policy_invalid");
        }
    }
}
