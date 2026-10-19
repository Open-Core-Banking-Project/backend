using System.Linq;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Persistence;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.RiskReporting.Contracts;
using CoreBanking.RiskReporting.Infrastructure.Institutions;
using CoreBanking.RiskReporting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CoreBanking.RiskReporting.UnitTests;

public sealed class InstitutionProvisioningTests
{
    [Fact]
    public void Consumer_supports_the_activation_event_on_the_institution_topic() =>
        Assert.Equal("institution_activated", InstitutionProvisioningConsumer.ActivationEventType);

    [Fact]
    public void Consumer_supports_the_suspension_event_on_the_institution_topic() =>
        Assert.Equal("institution_suspended", InstitutionProvisioningConsumer.SuspensionEventType);

    [Fact]
    public void Consumer_uses_the_versioned_core_ledger_institution_topic() =>
        Assert.Equal("corebanking.core-ledger.institution.v1", InstitutionProvisioningConsumer.TopicName);

    [Fact]
    public void Acknowledgement_contract_preserves_source_event_evidence()
    {
        InstitutionBoundaryInitialisedV1 payload = new("institution-1", "risk-reporting", 1, "source-event-1");
        Assert.Equal(payload, EventContract.Deserialize<InstitutionBoundaryInitialisedV1>(EventContract.Serialize(payload)));
    }

    [Fact]
    public void Every_tenant_owned_record_has_a_fail_closed_filter()
    {
        using RiskReportingDbContext context = CreateContext();
        IEntityType[] entities = context.Model.GetEntityTypes().Where(entity => typeof(ITenantOwnedEntity).IsAssignableFrom(entity.ClrType)).ToArray();
        Assert.NotEmpty(entities);
        Assert.All(entities, entity => Assert.NotEmpty(entity.GetDeclaredQueryFilters()));
    }

    [Fact]
    public void Boundary_source_event_is_unique()
    {
        using RiskReportingDbContext context = CreateContext();
        IEntityType boundary = context.Model.FindEntityType(typeof(InstitutionBoundaryRecord))!;
        Assert.Contains(boundary.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(["SourceEventId"]));
    }

    private static RiskReportingDbContext CreateContext()
    {
        DbContextOptions<RiskReportingDbContext> options = new DbContextOptionsBuilder<RiskReportingDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;Password=model").Options;
        return new RiskReportingDbContext(options, new AmbientTenantContext());
    }
}
