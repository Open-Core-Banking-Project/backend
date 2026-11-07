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
/// Resolves the effective, published <c>statement.policy</c> version. A missing, overlapping, superseded or corrupt
/// policy yields no policy, so a statement request is refused instead of defaulting a format or a retention period.
/// Decision: P1-E04-D15.
/// </summary>
public sealed class StatementPolicySource : IStatementPolicySource
{
    private readonly CoreLedgerDbContext _context;

    public StatementPolicySource(CoreLedgerDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<StatementPolicyLookup> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);

        ConfigurationSetRecord? set = await _context.ConfigurationSets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId.Value && record.Area == StatementPolicy.Area,
                cancellationToken)
            .ConfigureAwait(false);
        if (set is null)
        {
            return StatementPolicyLookup.Rejected(AccountStatementPolicy.PolicyMissingCode);
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
            return StatementPolicyLookup.Rejected(AccountStatementPolicy.PolicyMissingCode);
        }

        if (effective.Length > 1)
        {
            return StatementPolicyLookup.Rejected("statement_policy_ambiguous");
        }

        ConfigurationPublicationRecord publication = effective[0];
        ConfigurationVersionRecord? version = await _context.ConfigurationVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId.Value && record.VersionId == publication.VersionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (version is null
            || !string.Equals(version.Schema, StatementPolicy.Schema, StringComparison.Ordinal)
            || !string.Equals(version.ConfigurationSetId, set.ConfigurationSetId, StringComparison.Ordinal)
            || !string.Equals(version.Status, "published", StringComparison.Ordinal)
            || !string.Equals(version.ContentHash, publication.ContentHash, StringComparison.Ordinal)
            || version.EffectiveFrom != publication.EffectiveFrom
            || version.EffectiveTo != publication.EffectiveTo)
        {
            return StatementPolicyLookup.Rejected("statement_policy_integrity_failed");
        }

        ConfigurationContent content = ConfigurationContent.Create(version.ContentJson);
        if (!string.Equals(content.Hash, version.ContentHash, StringComparison.Ordinal))
        {
            return StatementPolicyLookup.Rejected("statement_policy_integrity_failed");
        }

        try
        {
            return StatementPolicyLookup.Found(
                StatementPolicy.Parse(version.ContentJson), version.VersionId, version.ContentHash);
        }
        catch (ArgumentException)
        {
            return StatementPolicyLookup.Rejected("statement_policy_invalid");
        }
    }
}

/// <summary>
/// The in-process ledger answer to the statement query. The posted-history query belongs to the Ledger epic
/// (P1-E05), and a statement must never be built from a partial read, so this adapter answers unavailable with
/// <c>statement_source_unavailable</c> rather than substituting a summary, a cache or a re-derived balance.
/// Decision: P1-E04-D15.
/// </summary>
public sealed class LedgerStatementQuerySource : ILedgerStatementQuerySource
{
    private readonly CoreLedgerDbContext _context;
    private readonly TimeProvider _timeProvider;

    public LedgerStatementQuerySource(CoreLedgerDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<LedgerStatementQueryResult> GetAsync(
        InstitutionId institutionId,
        string accountId,
        string currencyCode,
        DateOnly periodFrom,
        DateOnly periodTo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        LedgerAccountRecord? ledgerAccount = await _context.LedgerAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.AccountId == accountId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (ledgerAccount is null)
        {
            return new LedgerStatementQueryResult(
                false,
                "ledger_initialisation_unproven",
                string.Empty,
                string.Empty,
                null,
                null,
                0);
        }

        return new LedgerStatementQueryResult(
            Available: false,
            UnavailableCode: AccountStatementPolicy.SourceUnavailableCode,
            QueryReference: $"ledger:{ledgerAccount.LedgerAccountId}:statement:{periodFrom:yyyyMMdd}-{periodTo:yyyyMMdd}:{now:O}",
            IntegrityReference: string.Empty,
            OpeningBalance: null,
            ClosingBalance: null,
            MovementCount: 0);
    }
}
