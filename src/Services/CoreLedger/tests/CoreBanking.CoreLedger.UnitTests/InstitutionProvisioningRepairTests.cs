using System;
using System.Globalization;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class InstitutionProvisioningRepairTests
{
    [Fact]
    public void Pending_incomplete_run_is_replay_eligible()
    {
        InstitutionProvisioningRepairPolicy.EnsureReplayEligible(
            "provisioning",
            ["initialised", "pending"]);
    }

    [Fact]
    public void Failed_boundary_requires_owning_service_recovery()
    {
        InstitutionProvisioningRepairRejectedException exception = Assert.Throws<InstitutionProvisioningRepairRejectedException>(
            () => InstitutionProvisioningRepairPolicy.EnsureReplayEligible(
                "provisioning_failed",
                ["initialised", "failed"]));

        Assert.Equal("downstream_repair_required", exception.Code);
    }

    [Theory]
    [InlineData("pending_activation")]
    [InlineData("completed")]
    public void Ready_or_completed_run_cannot_be_replayed(string status)
    {
        InstitutionProvisioningRepairRejectedException exception = Assert.Throws<InstitutionProvisioningRepairRejectedException>(
            () => InstitutionProvisioningRepairPolicy.EnsureReplayEligible(status, ["initialised"]));

        Assert.Equal("provisioning_repair_not_eligible", exception.Code);
    }

    [Fact]
    public void Institution_audit_mapping_is_tenant_scoped_and_append_only()
    {
        AmbientTenantContext tenant = new();
        tenant.Establish(InstitutionId.From("institution-1"));
        DbContextOptions<CoreLedgerDbContext> options = new DbContextOptionsBuilder<CoreLedgerDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;Password=model")
            .Options;
        using CoreLedgerDbContext context = new(options, tenant);
        Microsoft.EntityFrameworkCore.Metadata.IEntityType entity = context.Model.FindEntityType(typeof(InstitutionAuditRecord))!;

        Assert.Equal("institution_audit_events", entity.GetTableName());
        Assert.Equal("audit", entity.GetSchema());
        Assert.NotEmpty(entity.GetDeclaredQueryFilters());

        InstitutionAuditRecord evidence = CreateAudit();
        context.Attach(evidence);
        evidence.Outcome = "failed";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains("append-only", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Audit_contract_excludes_secret_and_payload_fields()
    {
        string[] properties = typeof(InstitutionAuditRecord).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain(properties, name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Payload", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(nameof(InstitutionAuditRecord.Permission), properties);
        Assert.Contains(nameof(InstitutionAuditRecord.CorrelationId), properties);
    }

    private static InstitutionAuditRecord CreateAudit() => new()
    {
        InstitutionId = "institution-1",
        AuditEventId = "audit-1",
        ActorId = "operator-1",
        ActorType = "human",
        Permission = "platform.provisioning.repair",
        Action = "institution.provisioning.replay_requested",
        ResourceType = "provisioning_run",
        ResourceId = "run-1",
        Outcome = "succeeded",
        Reason = "stuck delivery",
        CorrelationId = "correlation-1",
        OccurredAt = DateTimeOffset.Parse("2026-10-26T08:00:00Z", CultureInfo.InvariantCulture),
    };
}
