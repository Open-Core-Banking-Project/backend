using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Application.Persistence;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class CoreLedgerPersistenceModelTests
{
    [Fact]
    public void Every_tenant_owned_institution_entity_has_a_fail_closed_query_filter()
    {
        using CoreLedgerDbContext context = CreateContext();

        IEntityType[] tenantEntities = context.Model.GetEntityTypes()
            .Where(type => typeof(ITenantOwnedEntity).IsAssignableFrom(type.ClrType))
            .ToArray();

        Assert.NotEmpty(tenantEntities);
        Assert.All(tenantEntities, entity => Assert.NotEmpty(entity.GetDeclaredQueryFilters()));
    }

    [Fact]
    public void Provisioning_steps_use_an_institution_scoped_key_and_foreign_key()
    {
        using CoreLedgerDbContext context = CreateContext();
        IEntityType step = context.Model.FindEntityType(typeof(ProvisioningStepRecord))
            ?? throw new InvalidOperationException("Provisioning step mapping is missing.");

        Assert.Equal(
            ["InstitutionId", "ProvisioningRunId", "ServiceId"],
            step.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(
            step.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(["InstitutionId", "ProvisioningRunId"]));
    }

    [Fact]
    public void Provisioning_steps_persist_acknowledgement_identity_and_time()
    {
        using CoreLedgerDbContext context = CreateContext();
        IEntityType step = context.Model.FindEntityType(typeof(ProvisioningStepRecord))!;

        Assert.Equal(
            "acknowledgement_event_id",
            step.FindProperty(nameof(ProvisioningStepRecord.AcknowledgementEventId))!.GetColumnName());
        Assert.Equal(
            "acknowledged_at",
            step.FindProperty(nameof(ProvisioningStepRecord.AcknowledgedAt))!.GetColumnName());
    }

    [Fact]
    public void Institution_code_and_active_provisioning_run_are_unique()
    {
        using CoreLedgerDbContext context = CreateContext();
        IEntityType institution = context.Model.FindEntityType(typeof(InstitutionRecord))!;
        IEntityType run = context.Model.FindEntityType(typeof(ProvisioningRunRecord))!;

        Assert.Contains(institution.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(["Code"]));
        Assert.Contains(run.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["InstitutionId"])
            && index.GetFilter() is not null);
    }

    [Fact]
    public void Outbox_event_identity_is_unique_inside_the_institution_boundary()
    {
        using CoreLedgerDbContext context = CreateContext();
        IEntityType outbox = context.Model.FindEntityType(typeof(OutboxRecord))!;

        Assert.Contains(outbox.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(["InstitutionId", "EventId"]));
    }

    [Fact]
    public void Institution_lifecycle_uses_an_optimistic_concurrency_token_and_actor_evidence()
    {
        using CoreLedgerDbContext context = CreateContext();
        IEntityType institution = context.Model.FindEntityType(typeof(InstitutionRecord))!;

        Assert.True(institution.FindProperty(nameof(InstitutionRecord.Version))!.IsConcurrencyToken);
        Assert.Equal(
            "created_by_actor_id",
            institution.FindProperty(nameof(InstitutionRecord.CreatedByActorId))!.GetColumnName());
        Assert.Equal(
            "activated_by_actor_id",
            institution.FindProperty(nameof(InstitutionRecord.ActivatedByActorId))!.GetColumnName());
    }

    [Fact]
    public void Regions_and_branches_are_institution_scoped_with_unique_codes_and_history()
    {
        using CoreLedgerDbContext context = CreateContext();
        IEntityType region = context.Model.FindEntityType(typeof(RegionRecord))!;
        IEntityType branch = context.Model.FindEntityType(typeof(BranchRecord))!;
        IEntityType history = context.Model.FindEntityType(typeof(OrganisationStatusHistoryRecord))!;

        Assert.Equal(["InstitutionId", "RegionId"], region.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Equal(["InstitutionId", "BranchId"], branch.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Contains(region.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual(["InstitutionId", "Code"]));
        Assert.Contains(branch.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual(["InstitutionId", "Code"]));
        Assert.Contains(branch.GetForeignKeys(), foreignKey => foreignKey.Properties.Select(x => x.Name).SequenceEqual(["InstitutionId", "RegionId"]));
        Assert.True(region.FindProperty(nameof(RegionRecord.Version))!.IsConcurrencyToken);
        Assert.True(branch.FindProperty(nameof(BranchRecord.Version))!.IsConcurrencyToken);
        Assert.Equal("organisation_status_history", history.GetTableName());
    }

    [Fact]
    public void Calendar_versions_and_business_date_controls_are_tenant_scoped_and_concurrency_safe()
    {
        using CoreLedgerDbContext context = CreateContext();
        IEntityType version = context.Model.FindEntityType(typeof(CalendarVersionRecord))!;
        IEntityType day = context.Model.FindEntityType(typeof(CalendarDayRecord))!;
        IEntityType control = context.Model.FindEntityType(typeof(BusinessDateControlRecord))!;
        IEntityType request = context.Model.FindEntityType(typeof(BusinessDateAdvanceRequestRecord))!;
        IEntityType history = context.Model.FindEntityType(typeof(BusinessDateHistoryRecord))!;
        IEntityType calendar = context.Model.FindEntityType(typeof(BusinessCalendarRecord))!;

        Assert.Equal(["InstitutionId", "VersionId"], version.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Contains(version.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual(["InstitutionId", "CalendarId", "VersionNumber"]));
        Assert.Equal(["InstitutionId", "VersionId", "Date"], day.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.True(version.FindProperty(nameof(CalendarVersionRecord.Version))!.IsConcurrencyToken);
        Assert.True(calendar.FindProperty(nameof(BusinessCalendarRecord.Version))!.IsConcurrencyToken);
        Assert.True(control.FindProperty(nameof(BusinessDateControlRecord.Version))!.IsConcurrencyToken);
        Assert.True(request.FindProperty(nameof(BusinessDateAdvanceRequestRecord.Version))!.IsConcurrencyToken);
        Assert.Contains(history.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual(["InstitutionId", "RequestId"]));
    }

    [Fact]
    public void Configuration_lifecycle_is_tenant_scoped_versioned_and_hash_bound()
    {
        using CoreLedgerDbContext context = CreateContext();
        IEntityType set = context.Model.FindEntityType(typeof(ConfigurationSetRecord))!;
        IEntityType version = context.Model.FindEntityType(typeof(ConfigurationVersionRecord))!;
        IEntityType simulation = context.Model.FindEntityType(typeof(ConfigurationSimulationRecord))!;
        IEntityType publication = context.Model.FindEntityType(typeof(ConfigurationPublicationRecord))!;

        Assert.Equal(["InstitutionId", "ConfigurationSetId"], set.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Contains(set.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual(["InstitutionId", "Area", "Scope"]));
        Assert.True(set.FindProperty(nameof(ConfigurationSetRecord.Version))!.IsConcurrencyToken);
        Assert.Equal(["InstitutionId", "VersionId"], version.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.True(version.FindProperty(nameof(ConfigurationVersionRecord.Version))!.IsConcurrencyToken);
        Assert.Equal("jsonb", version.FindProperty(nameof(ConfigurationVersionRecord.ContentJson))!.GetColumnType());
        Assert.Contains(version.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual(["InstitutionId", "ConfigurationSetId", "VersionNumber"]));
        Assert.Contains(simulation.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual(["InstitutionId", "VersionId", "InputHash"]));
        Assert.Contains(publication.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual(["InstitutionId", "ConfigurationSetId", "EffectiveFrom"]));
        Assert.NotNull(version.FindProperty(nameof(ConfigurationVersionRecord.EffectiveTo)));
        Assert.NotNull(publication.FindProperty(nameof(ConfigurationPublicationRecord.EffectiveTo)));
        Assert.DoesNotContain(version.GetProperties(), property =>
            property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("PrivateKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Savings_products_have_stable_tenant_scoped_identity_and_configuration_ownership()
    {
        using CoreLedgerDbContext context = CreateContext();
        IEntityType product = context.Model.FindEntityType(typeof(ProductRecord))!;

        Assert.Equal("product", product.GetSchema());
        Assert.Equal("account_products", product.GetTableName());
        Assert.Equal(["InstitutionId", "ProductId"], product.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Contains(product.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual(["InstitutionId", "Code"]));
        Assert.Contains(product.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Select(x => x.Name).SequenceEqual(["InstitutionId", "ConfigurationSetId"]));
        Assert.True(product.FindProperty(nameof(ProductRecord.Version))!.IsConcurrencyToken);
    }

    private static CoreLedgerDbContext CreateContext()
    {
        DbContextOptions<CoreLedgerDbContext> options = new DbContextOptionsBuilder<CoreLedgerDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;Password=model")
            .Options;

        return new CoreLedgerDbContext(options, new AmbientTenantContext());
    }
}
