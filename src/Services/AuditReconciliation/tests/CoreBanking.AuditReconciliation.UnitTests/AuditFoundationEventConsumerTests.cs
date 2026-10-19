using System;
using System.Linq;
using System.Text.Json;
using CoreBanking.AuditReconciliation.Contracts;
using CoreBanking.AuditReconciliation.Infrastructure.Foundation;
using CoreBanking.AuditReconciliation.Infrastructure.Institutions;
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
    public void Provisioning_consumer_supports_the_activation_event_on_the_institution_topic() =>
        Assert.Equal("institution_activated", InstitutionProvisioningConsumer.ActivationEventType);

    [Fact]
    public void Consumer_supports_the_suspension_event_on_the_institution_topic() =>
        Assert.Equal("institution_suspended", InstitutionProvisioningConsumer.SuspensionEventType);

    [Fact]
    public void Consumer_subscribes_to_the_versioned_identity_access_foundation_topic()
    {
        Assert.Equal("corebanking.identity-access.foundation.v1", AuditFoundationEventConsumer.TopicName);
    }

    [Fact]
    public void Provisioning_consumer_subscribes_to_the_versioned_core_ledger_institution_topic()
    {
        Assert.Equal("corebanking.core-ledger.institution.v1", InstitutionProvisioningConsumer.TopicName);
    }

    [Fact]
    public void Provisioning_acknowledgement_carries_service_and_source_evidence()
    {
        InstitutionBoundaryInitialisedV1 payload = new(
            "institution-1",
            "audit-reconciliation",
            1,
            "source-event-1");

        string json = EventContract.Serialize(payload);
        InstitutionBoundaryInitialisedV1 restored =
            EventContract.Deserialize<InstitutionBoundaryInitialisedV1>(json)!;

        Assert.Equal(payload, restored);
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
        Assert.Contains("integration.untrusted_message_quarantine", tables);
        Assert.Contains("platform.institution_boundaries", tables);

        IEntityType audit = model.FindEntityType(typeof(AuditEvidenceRecord))!;
        Assert.Contains("InstitutionId", audit.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.NotEmpty(audit.GetDeclaredQueryFilters());
        Assert.DoesNotContain(audit.GetProperties(), property =>
            property.Name.Contains("Description", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase));

        IEntityType quarantine = model.FindEntityType(typeof(UntrustedMessageQuarantineRecord))!;
        Assert.Empty(quarantine.GetDeclaredQueryFilters());
        Assert.DoesNotContain(quarantine.GetProperties(), property =>
            string.Equals(property.Name, "InstitutionId", StringComparison.Ordinal));

        IEntityType boundary = model.FindEntityType(typeof(InstitutionBoundaryRecord))!;
        Assert.NotEmpty(boundary.GetDeclaredQueryFilters());
        Assert.Contains(boundary.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["SourceEventId"]));
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
