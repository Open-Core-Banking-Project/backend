using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CoreBanking.CoreLedger.Application.Products;

/// <summary>One configuration version as reconciliation reads it.</summary>
public sealed record ConfigurationVersionFacts(
    string VersionId,
    string ConfigurationSetId,
    string Area,
    string Schema,
    string Status,
    string ContentHash,
    string? ApprovalReference,
    string? ApprovedByActorId,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo);

/// <summary>One publication as reconciliation reads it.</summary>
public sealed record ConfigurationPublicationFacts(
    string PublicationId,
    string ConfigurationSetId,
    string VersionId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

/// <summary>A bounded, identifier-only finding. No payload, hash of a customer field or PII.</summary>
public sealed record ConfigurationReconciliationFinding(string Code, string ConfigurationSetId, string Reference);

/// <summary>The result of one reconciliation pass. Identifiers stay bounded and no payload is included.</summary>
public sealed record ConfigurationReconciliationReport(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ConfigurationReconciliationFinding> Findings,
    long PendingOutbox,
    long OldestPendingOutboxAgeSeconds,
    IReadOnlyList<AccountNumberSchemeDepletion>? AccountNumberSchemes = null)
{
    public bool IsHealthy => Findings.Count == 0 && PendingOutbox == 0;
}

/// <summary>Number depletion of one published scheme instance. Decision: P1-E04-D06.</summary>
public sealed record AccountNumberSchemeDepletion(
    string SchemeVersionId,
    string SchemeId,
    long Capacity,
    long Allocated,
    long Available,
    int DepletionBand);

/// <summary>One account as account reconciliation reads it.</summary>
public sealed record AccountReconciliationFacts(
    string AccountId,
    string AccountApplicationId,
    string Status,
    string LedgerInitialisationStatus,
    string? LedgerAccountReference);

/// <summary>One application as account reconciliation reads it.</summary>
public sealed record ApplicationReconciliationFacts(
    string ApplicationId,
    string Status);

/// <summary>One closure proposal as account reconciliation reads it. Decision: P1-E04-D13.</summary>
public sealed record ClosureReconciliationFacts(
    string ProposalId,
    string AccountId,
    string Status,
    decimal LedgerBalance,
    string? ConditionSnapshotHash);

/// <summary>
/// Detects account states that must never survive a settled system: an open or active account without a
/// proven ledger initialisation, an approved application that never produced an account, a closed account whose
/// recorded closure balance was not zero, and a closed account with no condition snapshot behind it.
/// Decisions: P1-E04-D09, P1-E04-D13.
/// </summary>
public static class AccountReconciliationPolicy
{
    public const string MissingLedgerInitialisationCode = "account_without_ledger_initialisation";
    public const string ApprovedWithoutAccountCode = "approved_application_without_account";
    public const string ClosedWithResidualBalanceCode = "closed_account_with_residual_balance";
    public const string ClosureWithoutSnapshotCode = "closure_without_condition_snapshot";

    private static readonly string[] InitialisedStatuses = ["Open", "Active", "Restricted", "Frozen", "Dormant", "PendingClosure", "Closed"];

    public static IReadOnlyList<ConfigurationReconciliationFinding> Evaluate(
        IReadOnlyCollection<AccountReconciliationFacts> accounts,
        IReadOnlyCollection<ApplicationReconciliationFacts> applications,
        IReadOnlyCollection<ClosureReconciliationFacts>? closures = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(applications);
        closures ??= [];

        List<ConfigurationReconciliationFinding> findings = [];
        findings.AddRange(accounts
            .Where(account => InitialisedStatuses.Contains(account.Status, StringComparer.Ordinal)
                && (!string.Equals(account.LedgerInitialisationStatus, "initialised", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(account.LedgerAccountReference)))
            .OrderBy(account => account.AccountId, StringComparer.Ordinal)
            .Take(ConfigurationReconciliationPolicy.MaximumFindingsPerCode)
            .Select(account => new ConfigurationReconciliationFinding(
                MissingLedgerInitialisationCode,
                account.AccountApplicationId,
                account.AccountId)));
        findings.AddRange(applications
            .Where(application => string.Equals(application.Status, "Approved", StringComparison.Ordinal))
            .OrderBy(application => application.ApplicationId, StringComparer.Ordinal)
            .Take(ConfigurationReconciliationPolicy.MaximumFindingsPerCode)
            .Select(application => new ConfigurationReconciliationFinding(
                ApprovedWithoutAccountCode,
                application.ApplicationId,
                application.ApplicationId)));
        findings.AddRange(closures
            .Where(closure => string.Equals(closure.Status, "closed", StringComparison.Ordinal)
                && closure.LedgerBalance != 0m)
            .OrderBy(closure => closure.ProposalId, StringComparer.Ordinal)
            .Take(ConfigurationReconciliationPolicy.MaximumFindingsPerCode)
            .Select(closure => new ConfigurationReconciliationFinding(
                ClosedWithResidualBalanceCode,
                closure.ProposalId,
                closure.AccountId)));
        string[] snapshotted = [.. closures
            .Where(closure => !string.IsNullOrWhiteSpace(closure.ConditionSnapshotHash))
            .Select(closure => closure.AccountId)];
        findings.AddRange(accounts
            .Where(account => string.Equals(account.Status, "Closed", StringComparison.Ordinal)
                && !snapshotted.Contains(account.AccountId, StringComparer.Ordinal))
            .OrderBy(account => account.AccountId, StringComparer.Ordinal)
            .Take(ConfigurationReconciliationPolicy.MaximumFindingsPerCode)
            .Select(account => new ConfigurationReconciliationFinding(
                ClosureWithoutSnapshotCode,
                account.AccountApplicationId,
                account.AccountId)));
        return findings;
    }
}

public interface IConfigurationReconciliationService
{
    Task<ConfigurationReconciliationReport> ReconcileAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Detects configuration states that must never coexist: a publication without its version, a hash
/// mismatch, a published version with no publication, an approved version with no approval evidence,
/// and overlapping effective periods inside one set. Decision: P1-E04 slice 14.
/// </summary>
public static class ConfigurationReconciliationPolicy
{
    /// <summary>Each code contributes at most this many findings so a report stays bounded.</summary>
    public const int MaximumFindingsPerCode = 20;

    public static IReadOnlyList<ConfigurationReconciliationFinding> Evaluate(
        IReadOnlyCollection<ConfigurationVersionFacts> versions,
        IReadOnlyCollection<ConfigurationPublicationFacts> publications)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(publications);

        Dictionary<string, ConfigurationVersionFacts> byVersion = versions
            .GroupBy(version => version.VersionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        List<ConfigurationReconciliationFinding> findings = [];

        foreach (ConfigurationPublicationFacts publication in publications)
        {
            if (!byVersion.TryGetValue(publication.VersionId, out ConfigurationVersionFacts? version))
            {
                Add(findings, "publication_version_missing", publication.ConfigurationSetId, publication.PublicationId);
                continue;
            }

            if (!string.Equals(publication.ContentHash, version.ContentHash, StringComparison.Ordinal))
            {
                Add(findings, "publication_hash_mismatch", publication.ConfigurationSetId, publication.PublicationId);
            }
        }

        HashSet<string> publishedVersionIds = publications
            .Select(publication => publication.VersionId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (ConfigurationVersionFacts version in versions)
        {
            if (string.Equals(version.Status, "published", StringComparison.Ordinal)
                && !publishedVersionIds.Contains(version.VersionId))
            {
                Add(findings, "published_version_without_publication", version.ConfigurationSetId, version.VersionId);
            }

            if (string.Equals(version.Status, "approved", StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(version.ApprovalReference) || string.IsNullOrWhiteSpace(version.ApprovedByActorId)))
            {
                Add(findings, "approved_without_approval_evidence", version.ConfigurationSetId, version.VersionId);
            }
        }

        foreach (IGrouping<string, ConfigurationPublicationFacts> set in publications.GroupBy(
            publication => publication.ConfigurationSetId,
            StringComparer.Ordinal))
        {
            ConfigurationPublicationFacts[] ordered = [.. set.OrderBy(publication => publication.EffectiveFrom)];
            for (int index = 1; index < ordered.Length; index++)
            {
                ConfigurationPublicationFacts previous = ordered[index - 1];
                ConfigurationPublicationFacts current = ordered[index];
                if (previous.EffectiveTo is null || current.EffectiveFrom < previous.EffectiveTo)
                {
                    Add(findings, "overlapping_publication_periods", set.Key, current.PublicationId);
                }
            }
        }

        return findings;
    }

    private static void Add(
        List<ConfigurationReconciliationFinding> findings,
        string code,
        string configurationSetId,
        string reference)
    {
        if (findings.Count(finding => string.Equals(finding.Code, code, StringComparison.Ordinal)) >= MaximumFindingsPerCode)
        {
            return;
        }

        findings.Add(new ConfigurationReconciliationFinding(code, configurationSetId, reference));
    }
}
