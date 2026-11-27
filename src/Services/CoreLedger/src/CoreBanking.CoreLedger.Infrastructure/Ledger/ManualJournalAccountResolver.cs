using System;
using System.Collections.Generic;
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
/// Resolves each manual-journal ledger account against the exact effective published chart. A missing, ambiguous or
/// tampered chart, an unknown account code or an account outside its chart scope fails closed instead of guessing a
/// class. Decision: P1-E05-D11.
/// </summary>
public sealed class ManualJournalAccountResolver : IManualJournalAccountResolver
{
    private readonly CoreLedgerDbContext _context;

    public ManualJournalAccountResolver(CoreLedgerDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<ManualJournalAccountLookup> ResolveAsync(
        InstitutionId institutionId,
        IReadOnlyList<string> ledgerAccountIds,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(ledgerAccountIds);

        string[] ids = ledgerAccountIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
        {
            return ManualJournalAccountLookup.Rejected("manual_journal_lines_insufficient");
        }

        LedgerAccountRecord[] accounts = await _context.LedgerAccounts
            .AsNoTracking()
            .Where(record => record.InstitutionId == institutionId.Value && ids.Contains(record.LedgerAccountId))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (accounts.Length != ids.Length)
        {
            return ManualJournalAccountLookup.Rejected("ledger_account_not_found");
        }

        if (accounts.Any(record => !string.Equals(record.Status, "initialised", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(record.GlAccountCode)
            || string.IsNullOrWhiteSpace(record.LegalEntityCode)))
        {
            return ManualJournalAccountLookup.Rejected("ledger_account_not_postable");
        }

        EffectiveChart chart;
        try
        {
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
                return ManualJournalAccountLookup.Rejected("chart_version_not_effective");
            }

            if (candidates.Length > 1)
            {
                return ManualJournalAccountLookup.Rejected("chart_version_ambiguous");
            }

            if (!string.Equals(candidates[0].VersionHash, candidates[0].PublicationHash, StringComparison.Ordinal))
            {
                return ManualJournalAccountLookup.Rejected("chart_content_integrity_failed");
            }

            ConfigurationContent content = LedgerConfigurationContent.ValidateChart(candidates[0].ContentJson);
            if (!string.Equals(candidates[0].VersionHash, content.Hash, StringComparison.Ordinal))
            {
                return ManualJournalAccountLookup.Rejected("chart_content_integrity_failed");
            }

            chart = new EffectiveChart(candidates[0].VersionId, content.CanonicalJson);
        }
        catch (ArgumentException)
        {
            return ManualJournalAccountLookup.Rejected("chart_content_integrity_failed");
        }

        string chartLegalEntity = LedgerConfigurationContent.ResolveLegalEntityCode(chart.ContentJson);
        List<ManualJournalAccountClassification> resolved = new(accounts.Length);
        foreach (LedgerAccountRecord account in accounts.OrderBy(record => record.LedgerAccountId, StringComparer.Ordinal))
        {
            if (!string.Equals(account.LegalEntityCode, chartLegalEntity, StringComparison.Ordinal))
            {
                return ManualJournalAccountLookup.Rejected("ledger_account_scope_mismatch");
            }

            LedgerAccountRole role;
            try
            {
                role = LedgerConfigurationContent.ResolveAccountRole(chart.ContentJson, account.GlAccountCode!);
            }
            catch (LedgerMappingException)
            {
                return ManualJournalAccountLookup.Rejected("ledger_account_code_unknown");
            }
            catch (ArgumentException)
            {
                return ManualJournalAccountLookup.Rejected("chart_content_integrity_failed");
            }

            resolved.Add(new ManualJournalAccountClassification(
                account.LedgerAccountId,
                account.AccountId,
                account.GlAccountCode!,
                role,
                account.LegalEntityCode!,
                account.CurrencyCode));
        }

        return ManualJournalAccountLookup.Resolved(resolved);
    }

    private sealed record EffectiveChart(string VersionId, string ContentJson);
}
