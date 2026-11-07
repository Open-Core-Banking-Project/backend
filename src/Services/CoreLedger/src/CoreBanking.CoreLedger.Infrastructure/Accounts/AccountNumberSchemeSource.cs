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
/// Resolves the effective, published <c>institution.account-numbering</c> version. A missing,
/// overlapping, superseded or corrupt scheme yields no scheme so allocation fails closed rather than
/// inventing a format. Decision: P1-E04-D06.
/// </summary>
public sealed class AccountNumberSchemeSource : IAccountNumberSchemeSource
{
    private readonly CoreLedgerDbContext _context;

    public AccountNumberSchemeSource(CoreLedgerDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<AccountNumberSchemeLookup> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);

        ConfigurationSetRecord? set = await _context.ConfigurationSets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId.Value
                    && record.Area == AccountNumberScheme.Area,
                cancellationToken)
            .ConfigureAwait(false);
        if (set is null)
        {
            return AccountNumberSchemeLookup.Rejected("account_number_scheme_missing");
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
            return AccountNumberSchemeLookup.Rejected("account_number_scheme_missing");
        }

        if (effective.Length > 1)
        {
            return AccountNumberSchemeLookup.Rejected("account_number_scheme_ambiguous");
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
            || !string.Equals(version.Schema, AccountNumberScheme.Schema, StringComparison.Ordinal)
            || !string.Equals(version.ConfigurationSetId, set.ConfigurationSetId, StringComparison.Ordinal)
            || !string.Equals(version.Status, "published", StringComparison.Ordinal)
            || !string.Equals(version.ContentHash, publication.ContentHash, StringComparison.Ordinal)
            || version.EffectiveFrom != publication.EffectiveFrom
            || version.EffectiveTo != publication.EffectiveTo)
        {
            return AccountNumberSchemeLookup.Rejected("account_number_scheme_integrity_failed");
        }

        ConfigurationContent content = ConfigurationContent.Create(version.ContentJson);
        if (!string.Equals(content.Hash, version.ContentHash, StringComparison.Ordinal))
        {
            return AccountNumberSchemeLookup.Rejected("account_number_scheme_integrity_failed");
        }

        try
        {
            return AccountNumberSchemeLookup.Found(AccountNumberScheme.Parse(version.ContentJson), version.VersionId);
        }
        catch (ArgumentException)
        {
            return AccountNumberSchemeLookup.Rejected("account_number_scheme_invalid");
        }
    }
}
