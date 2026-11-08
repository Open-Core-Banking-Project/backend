using System;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Products;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.UnitTests.Persistence;

/// <summary>
/// One isolated institution in the real <c>core_ledger_db</c>, seeded with an active institution, a
/// region, a branch and one published product version, then removed when the suite finishes. Audit
/// rows are left in place because the application principal can only append them.
/// Executed by <c>scripts/db-verify.ps1</c>.
/// </summary>
internal sealed class CoreLedgerDatabaseScope : IDisposable
{
    internal const string ConnectionStringVariableName = "ConnectionStrings__CoreLedger";

    /// <summary>The instant every seeded record is effective from and every test clock reports.</summary>
    internal static readonly DateTimeOffset SeededAt = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly DbContextOptions<CoreLedgerDbContext> _options;

    internal CoreLedgerDatabaseScope()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringVariableName} to the application principal connection string, or run scripts/db-verify.ps1.");
        }

        string suffix = Guid.NewGuid().ToString("N")[..12];
        Institution = InstitutionId.From("ledger-db-" + suffix);
        InstitutionCode = "LED" + suffix[..6].ToUpperInvariant();
        RegionId = "region-" + suffix;
        BranchId = "branch-" + suffix;
        BranchCode = "BR" + suffix[..6].ToUpperInvariant();
        ProductId = "product-" + suffix;
        ProductCode = "SAVINGS-" + suffix[..6].ToUpperInvariant();
        ConfigurationSetId = "set-" + suffix;
        ProductVersionId = "version-" + suffix;
        PublicationId = "publication-" + suffix;

        _options = new DbContextOptionsBuilder<CoreLedgerDbContext>()
            .UseNpgsql(configured)
            .Options;

        Seed();
    }

    internal InstitutionId Institution { get; }

    internal string InstitutionCode { get; }

    internal string RegionId { get; }

    internal string BranchId { get; }

    internal string BranchCode { get; }

    internal string ProductId { get; }

    internal string ProductCode { get; }

    internal string ConfigurationSetId { get; }

    internal string ProductVersionId { get; }

    internal string PublicationId { get; }

    /// <summary>A tenant context already scoped to this suite's institution.</summary>
    internal AmbientTenantContext NewTenant()
    {
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(Institution);
        return tenantContext;
    }

    internal CoreLedgerDbContext CreateContext(AmbientTenantContext tenantContext) =>
        new(_options, tenantContext);

    internal CoreLedgerDbContext CreateContext() => CreateContext(NewTenant());

    public void Dispose()
    {
        using CoreLedgerDbContext context = CreateContext();

        context.AccountLienMovements.ExecuteDelete();
        context.AccountLiens.ExecuteDelete();
        context.AccountRestrictions.ExecuteDelete();
        context.AccountFreezes.ExecuteDelete();
        context.AccountBlocks.ExecuteDelete();
        context.AccountDormancyNotices.ExecuteDelete();
        context.AccountDormancyAssessments.ExecuteDelete();
        context.AccountDormancyLeases.ExecuteDelete();
        context.AccountClosureProposals.ExecuteDelete();
        context.AccountStatementArtifactReferences.ExecuteDelete();
        context.AccountStatementRequests.ExecuteDelete();
        context.LedgerAccounts.ExecuteDelete();
        context.AccountStatusHistory.ExecuteDelete();
        context.Accounts.ExecuteDelete();
        context.AccountEligibilityEvaluations.ExecuteDelete();
        context.AccountOpeningClaims.ExecuteDelete();
        context.AccountNumberRetirements.ExecuteDelete();
        context.AccountNumberReservations.ExecuteDelete();
        context.AccountApplications.ExecuteDelete();
        context.Products.ExecuteDelete();
        context.ConfigurationPublications.ExecuteDelete();
        context.ConfigurationVersions.ExecuteDelete();
        context.ConfigurationSets.ExecuteDelete();
        context.Branches.ExecuteDelete();
        context.Regions.ExecuteDelete();
        context.Institutions.ExecuteDelete();
        context.OutboxMessages.ExecuteDelete();
        context.InboxMessages.ExecuteDelete();
        context.IdempotencyRecords.ExecuteDelete();
        context.DeadLetters.ExecuteDelete();
    }

    private void Seed()
    {
        ProductEligibilityRules rules = ProductEligibilityRules.Create(
            "savings-standard",
            1,
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityAgeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any());

        ConfigurationContent content = rules.ToConfigurationContent();
        DateTimeOffset effectiveFrom = SeededAt.AddMinutes(-5);

        using CoreLedgerDbContext context = CreateContext();
        context.Institutions.Add(new InstitutionRecord
        {
            InstitutionId = Institution.Value,
            Code = InstitutionCode,
            LegalName = "Ledger database tests",
            CountryCode = "NG",
            BaseCurrency = "NGN",
            Status = "active",
            CreatedByActorId = "db-test",
            ActivatedByActorId = "db-test",
            Version = 1,
            CreatedAt = effectiveFrom,
            UpdatedAt = effectiveFrom,
        });

        context.Regions.Add(new RegionRecord
        {
            InstitutionId = Institution.Value,
            RegionId = RegionId,
            Code = "RG" + InstitutionCode[3..],
            Name = "Region for database tests",
            Status = "active",
            CreatedByActorId = "db-test",
            Version = 1,
            CreatedAt = effectiveFrom,
            UpdatedAt = effectiveFrom,
        });

        context.Branches.Add(new BranchRecord
        {
            InstitutionId = Institution.Value,
            BranchId = BranchId,
            RegionId = RegionId,
            Code = BranchCode,
            Name = "Branch for database tests",
            Status = "active",
            CreatedByActorId = "db-test",
            Version = 1,
            CreatedAt = effectiveFrom,
            UpdatedAt = effectiveFrom,
        });

        context.ConfigurationSets.Add(new ConfigurationSetRecord
        {
            InstitutionId = Institution.Value,
            ConfigurationSetId = ConfigurationSetId,
            Area = "product_eligibility",
            Scope = "institution",
            Version = 1,
            CreatedAt = effectiveFrom,
        });

        context.ConfigurationVersions.Add(new ConfigurationVersionRecord
        {
            InstitutionId = Institution.Value,
            VersionId = ProductVersionId,
            ConfigurationSetId = ConfigurationSetId,
            VersionNumber = 1,
            Schema = "product_eligibility.v1",
            ContentJson = content.CanonicalJson,
            ContentHash = content.Hash,
            Status = "published",
            CreatedByActorId = "db-test",
            ApprovedByActorId = "db-checker",
            ApprovalReference = "approval-db-test",
            EffectiveFrom = effectiveFrom,
            PublishedAt = effectiveFrom,
            Version = 1,
            CreatedAt = effectiveFrom,
        });

        context.ConfigurationPublications.Add(new ConfigurationPublicationRecord
        {
            InstitutionId = Institution.Value,
            PublicationId = PublicationId,
            ConfigurationSetId = ConfigurationSetId,
            VersionId = ProductVersionId,
            ContentHash = content.Hash,
            PublishedByActorId = "db-checker",
            ApprovalReference = "approval-db-test",
            EffectiveFrom = effectiveFrom,
            PublishedAt = effectiveFrom,
        });

        context.Products.Add(new ProductRecord
        {
            InstitutionId = Institution.Value,
            ProductId = ProductId,
            Code = ProductCode,
            Family = "savings",
            ConfigurationSetId = ConfigurationSetId,
            CreatedByActorId = "db-test",
            Version = 1,
            CreatedAt = effectiveFrom,
        });

        context.SaveChanges();
    }
}
