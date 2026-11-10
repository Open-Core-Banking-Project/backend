using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests.Persistence;

/// <summary>Effective chart selection and content integrity against the real Core Ledger store.</summary>
[Trait("Category", "Database")]
public sealed class LedgerChartClassificationDatabaseTests : IDisposable
{
    private const string ChartSetId = "ledger-chart-set";
    private const string ChartVersionId = "ledger-chart-version-1";
    private const string ChartPublicationId = "ledger-chart-publication-1";

    private readonly CoreLedgerDatabaseScope _scope = new();

    public void Dispose() => _scope.Dispose();

    [Fact]
    public async Task Effective_published_chart_resolves_customer_deposit_classification()
    {
        await SeedChartAsync(ChartJson, useInvalidHash: false);
        await using CoreLedgerDbContext context = _scope.CreateContext();
        LedgerChartClassificationSource source = new(context);

        LedgerChartClassificationLookup result = await source.ResolveAsync(
            _scope.Institution,
            "NGN",
            CoreLedgerDatabaseScope.SeededAt,
            CancellationToken.None);

        Assert.Null(result.RejectionCode);
        Assert.Equal(ChartVersionId, result.ChartVersionId);
        Assert.Equal("bank-ng", result.LegalEntityCode);
        Assert.Equal("210001", result.GlAccountCode);
    }

    [Fact]
    public async Task Stored_hash_that_does_not_match_chart_content_fails_closed()
    {
        await SeedChartAsync(ChartJson, useInvalidHash: true);
        await using CoreLedgerDbContext context = _scope.CreateContext();
        LedgerChartClassificationSource source = new(context);

        LedgerChartClassificationLookup result = await source.ResolveAsync(
            _scope.Institution,
            "NGN",
            CoreLedgerDatabaseScope.SeededAt,
            CancellationToken.None);

        Assert.Null(result.ChartVersionId);
        Assert.Equal("chart_content_integrity_failed", result.RejectionCode);
    }

    [Fact]
    public async Task Chart_outside_its_effective_window_fails_closed()
    {
        await SeedChartAsync(ChartJson, useInvalidHash: false, effectiveFrom: CoreLedgerDatabaseScope.SeededAt.AddMinutes(1));
        await using CoreLedgerDbContext context = _scope.CreateContext();
        LedgerChartClassificationSource source = new(context);

        LedgerChartClassificationLookup result = await source.ResolveAsync(
            _scope.Institution,
            "NGN",
            CoreLedgerDatabaseScope.SeededAt,
            CancellationToken.None);

        Assert.Null(result.ChartVersionId);
        Assert.Equal("chart_version_not_effective", result.RejectionCode);
    }

    private async Task SeedChartAsync(
        string json,
        bool useInvalidHash,
        DateTimeOffset? effectiveFrom = null)
    {
        ConfigurationContent content = LedgerConfigurationContent.ValidateChart(json);
        string storedHash = useInvalidHash ? new string('0', content.Hash.Length) : content.Hash;
        DateTimeOffset effective = effectiveFrom ?? CoreLedgerDatabaseScope.SeededAt.AddMinutes(-1);

        await using CoreLedgerDbContext context = _scope.CreateContext();
        context.ConfigurationSets.Add(new ConfigurationSetRecord
        {
            InstitutionId = _scope.Institution.Value,
            ConfigurationSetId = ChartSetId,
            Area = LedgerConfigurationContent.ChartArea,
            Scope = "institution",
            Version = 1,
            CreatedAt = effective,
        });
        context.ConfigurationVersions.Add(new ConfigurationVersionRecord
        {
            InstitutionId = _scope.Institution.Value,
            VersionId = ChartVersionId,
            ConfigurationSetId = ChartSetId,
            VersionNumber = 1,
            Schema = LedgerConfigurationContent.ChartSchema,
            ContentJson = content.CanonicalJson,
            ContentHash = storedHash,
            Status = "published",
            CreatedByActorId = "db-maker",
            ApprovedByActorId = "db-checker",
            ApprovalReference = "approval-ledger-chart",
            EffectiveFrom = effective,
            PublishedAt = effective,
            Version = 1,
            CreatedAt = effective,
        });
        context.ConfigurationPublications.Add(new ConfigurationPublicationRecord
        {
            InstitutionId = _scope.Institution.Value,
            PublicationId = ChartPublicationId,
            ConfigurationSetId = ChartSetId,
            VersionId = ChartVersionId,
            ContentHash = storedHash,
            PublishedByActorId = "db-checker",
            ApprovalReference = "approval-ledger-chart",
            EffectiveFrom = effective,
            PublishedAt = effective,
        });
        await context.SaveChangesAsync();
    }

    private const string ChartJson = """
        {
          "legalEntityCode": "bank-ng",
          "accounts": [
            {
              "code": "210001",
              "role": "customerDeposit",
              "normalSide": "credit",
              "currency": "NGN",
              "status": "active"
            }
          ]
        }
        """;
}
