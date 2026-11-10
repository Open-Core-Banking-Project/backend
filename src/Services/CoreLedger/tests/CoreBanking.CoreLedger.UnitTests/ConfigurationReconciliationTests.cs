using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Infrastructure.Products;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

/// <summary>
/// Reconciliation is the alerting contract for configuration integrity, so every finding code is
/// asserted directly and the report stays bounded. Decision: P1-E04 slice 14.
/// </summary>
public sealed class ConfigurationReconciliationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_consistent_set_produces_no_findings()
    {
        ConfigurationVersionFacts version = Version("version-1", "published");
        ConfigurationPublicationFacts publication = Publication("publication-1", "version-1", version.ContentHash);

        IReadOnlyList<ConfigurationReconciliationFinding> findings =
            ConfigurationReconciliationPolicy.Evaluate([version], [publication]);

        Assert.Empty(findings);
        Assert.True(new ConfigurationReconciliationReport(Now, findings, 0, 0).IsHealthy);
    }

    [Fact]
    public void A_publication_without_its_version_is_reported()
    {
        IReadOnlyList<ConfigurationReconciliationFinding> findings =
            ConfigurationReconciliationPolicy.Evaluate([], [Publication("publication-1", "version-1", "hash")]);

        Assert.Equal("publication_version_missing", Assert.Single(findings).Code);
    }

    [Fact]
    public void A_hash_mismatch_between_publication_and_version_is_reported()
    {
        ConfigurationVersionFacts version = Version("version-1", "published", contentHash: "hash-a");

        IReadOnlyList<ConfigurationReconciliationFinding> findings =
            ConfigurationReconciliationPolicy.Evaluate([version], [Publication("publication-1", "version-1", "hash-b")]);

        Assert.Equal("publication_hash_mismatch", Assert.Single(findings).Code);
    }

    [Fact]
    public void A_published_version_without_a_publication_is_reported()
    {
        IReadOnlyList<ConfigurationReconciliationFinding> findings =
            ConfigurationReconciliationPolicy.Evaluate([Version("version-1", "published")], []);

        Assert.Equal("published_version_without_publication", Assert.Single(findings).Code);
    }

    [Fact]
    public void An_approved_version_without_approval_evidence_is_reported()
    {
        ConfigurationVersionFacts version = Version("version-1", "approved") with
        {
            ApprovalReference = null,
            ApprovedByActorId = null,
        };

        IReadOnlyList<ConfigurationReconciliationFinding> findings =
            ConfigurationReconciliationPolicy.Evaluate([version], []);

        Assert.Equal("approved_without_approval_evidence", Assert.Single(findings).Code);
    }

    [Fact]
    public void Overlapping_publication_periods_and_open_ended_periods_are_reported()
    {
        ConfigurationVersionFacts first = Version("version-1", "published");
        ConfigurationVersionFacts second = Version("version-2", "published") with { ContentHash = "hash-2" };

        IReadOnlyList<ConfigurationReconciliationFinding> findings = ConfigurationReconciliationPolicy.Evaluate(
            [first, second],
            [
                Publication("publication-1", "version-1", first.ContentHash, effectiveTo: null),
                Publication("publication-2", "version-2", second.ContentHash, effectiveFrom: Now.AddDays(1)),
            ]);

        Assert.Contains(findings, finding => finding.Code == "overlapping_publication_periods");
    }

    [Fact]
    public void Adjacent_periods_are_not_an_overlap()
    {
        ConfigurationVersionFacts first = Version("version-1", "published");
        ConfigurationVersionFacts second = Version("version-2", "published") with { ContentHash = "hash-2" };

        IReadOnlyList<ConfigurationReconciliationFinding> findings = ConfigurationReconciliationPolicy.Evaluate(
            [first, second],
            [
                Publication("publication-1", "version-1", first.ContentHash, effectiveTo: Now.AddDays(1)),
                Publication("publication-2", "version-2", second.ContentHash, effectiveFrom: Now.AddDays(1)),
            ]);

        Assert.DoesNotContain(findings, finding => finding.Code == "overlapping_publication_periods");
    }

    [Fact]
    public void Findings_are_bounded_per_code()
    {
        ConfigurationPublicationFacts[] publications = Enumerable
            .Range(0, 25)
            .Select(index => Publication("publication-" + index, "missing-" + index, "hash"))
            .ToArray();

        IReadOnlyList<ConfigurationReconciliationFinding> findings =
            ConfigurationReconciliationPolicy.Evaluate([], publications);

        Assert.Equal(
            ConfigurationReconciliationPolicy.MaximumFindingsPerCode,
            findings.Count(finding => finding.Code == "publication_version_missing"));
    }

    [Fact]
    public void A_pending_outbox_makes_the_report_unhealthy()
    {
        ConfigurationReconciliationReport report = new(Now, [], PendingOutbox: 1, OldestPendingOutboxAgeSeconds: 30);

        Assert.False(report.IsHealthy);
        Assert.Equal(30, report.OldestPendingOutboxAgeSeconds);
    }

    [Fact]
    public void The_meter_name_is_shared_with_the_observability_pipeline()
    {
        Assert.Equal("CoreBanking.ProductAccounts", ProductAccountMetrics.MeterName);
        Assert.Equal(
            CoreBanking.BuildingBlocks.Application.Diagnostics.CoreBankingMeterNames.ProductAccounts,
            ProductAccountMetrics.MeterName);
        Assert.Equal(ProductAccountMetrics.MeterName, ProductAccountMetrics.Shared.Meter.Name);
    }

    private static ConfigurationVersionFacts Version(
        string versionId,
        string status,
        string contentHash = "hash-1") =>
        new(
            versionId,
            "set-1",
            "institution.currencies",
            "institution.currencies.v1",
            status,
            contentHash,
            "approval-1",
            "approver-1",
            Now,
            null);

    private static ConfigurationPublicationFacts Publication(
        string publicationId,
        string versionId,
        string contentHash,
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveTo = null) =>
        new(publicationId, "set-1", versionId, contentHash, effectiveFrom ?? Now.AddDays(-1), effectiveTo);
}
