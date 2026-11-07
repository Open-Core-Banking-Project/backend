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
