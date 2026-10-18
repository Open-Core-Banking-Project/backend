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

    private static CoreLedgerDbContext CreateContext()
    {
        DbContextOptions<CoreLedgerDbContext> options = new DbContextOptionsBuilder<CoreLedgerDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;Password=model")
            .Options;

        return new CoreLedgerDbContext(options, new AmbientTenantContext());
    }
}
