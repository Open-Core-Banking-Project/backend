using System;
using System.Linq;
using System.Text.Json;
using CoreBanking.AuditReconciliation.Contracts;
using CoreBanking.AuditReconciliation.Infrastructure.Foundation;
using CoreBanking.AuditReconciliation.Infrastructure.Persistence;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Persistence;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CoreBanking.AuditReconciliation.UnitTests;

[Trait("Category", "Unit")]
public sealed class AuditFoundationEventConsumerTests
{
    [Fact]
    public void Consumer_subscribes_to_the_versioned_identity_access_foundation_topic()
    {
        Assert.Equal("corebanking.identity-access.foundation.v1", AuditFoundationEventConsumer.TopicName);
    }

    [Fact]
    public void Foundation_event_payload_contains_only_stable_identifiers()
    {
        string json = EventContract.Serialize(new FoundationRecordCreatedV1("record-1", "actor-1"));
        using JsonDocument document = JsonDocument.Parse(json);
        string[] properties = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["actorId", "foundationRecordId"], properties);
    }

    [Fact]
    public void Audit_database_model_has_tenant_scoped_append_only_evidence_and_inbox_tables()
    {
        IModel model = CreateModel();
        string[] tables = model.GetEntityTypes()
            .Select(entity => $"{entity.GetSchema()}.{entity.GetTableName()}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("audit.events", tables);
        Assert.Contains("integration.inbox_messages", tables);

        IEntityType audit = model.FindEntityType(typeof(AuditEvidenceRecord))!;
        Assert.Contains("InstitutionId", audit.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.NotEmpty(audit.GetDeclaredQueryFilters());
        Assert.DoesNotContain(audit.GetProperties(), property =>
            property.Name.Contains("Description", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase));
    }

    private static IModel CreateModel()
    {
        DbContextOptionsBuilder<AuditReconciliationDbContext> options = new();
        options.UseNpgsql("Host=127.0.0.1;Port=5432;Database=model_only;Username=model_only;Password=model-only");

        using AuditReconciliationDbContext context = new(options.Options, new UnestablishedTenantContext());
        return context.GetService<IDesignTimeModel>().Model;
    }

    private sealed class UnestablishedTenantContext : ITenantContext
    {
        public InstitutionId? CurrentInstitution => null;

        public void Establish(InstitutionId institutionId) =>
            throw new NotSupportedException("No institution is available to this context.");

        public void Clear()
        {
        }
    }
}
