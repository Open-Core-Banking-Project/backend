using System;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Persistence;

[Trait("Category", "Database")]
public sealed class OperatorActionAuditRecordTests
{
    [Fact]
    public async Task Application_principal_can_append_operator_replay_evidence()
    {
        await using IdentityAccessDbContext context = CreateContext();
        string auditEventId = Guid.NewGuid().ToString("N");

        context.OperatorActionAuditEvents.Add(CreateRecord(auditEventId));
        await context.SaveChangesAsync();

        OperatorActionAuditRecord evidence = await context.OperatorActionAuditEvents
            .SingleAsync(record => record.AuditEventId == auditEventId);

        Assert.Equal("messaging.outbox.replay", evidence.Action);
        Assert.Equal("integration_event", evidence.ResourceType);
        Assert.Equal("operator-test", evidence.ActorId);
    }

    [Fact]
    public async Task Application_principal_cannot_update_operator_replay_evidence()
    {
        await using IdentityAccessDbContext context = CreateContext();
        string auditEventId = Guid.NewGuid().ToString("N");

        context.OperatorActionAuditEvents.Add(CreateRecord(auditEventId));
        await context.SaveChangesAsync();

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        OperatorActionAuditRecord evidence = await context.OperatorActionAuditEvents
            .SingleAsync(record => record.AuditEventId == auditEventId);
        evidence.Reason = "rewritten";

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Application_principal_cannot_delete_operator_replay_evidence()
    {
        await using IdentityAccessDbContext context = CreateContext();
        string auditEventId = Guid.NewGuid().ToString("N");

        context.OperatorActionAuditEvents.Add(CreateRecord(auditEventId));
        await context.SaveChangesAsync();

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        OperatorActionAuditRecord evidence = await context.OperatorActionAuditEvents
            .SingleAsync(record => record.AuditEventId == auditEventId);
        context.OperatorActionAuditEvents.Remove(evidence);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        await transaction.RollbackAsync();
    }

    private static OperatorActionAuditRecord CreateRecord(string auditEventId) => new()
    {
        AuditEventId = auditEventId,
        InstitutionId = "institution-test",
        ActorId = "operator-test",
        Action = "messaging.outbox.replay",
        ResourceType = "integration_event",
        ResourceId = Guid.NewGuid().ToString("N"),
        Reason = "verified broker recovery",
        CorrelationId = Guid.NewGuid().ToString("N"),
        OccurredAt = DateTimeOffset.UtcNow,
    };

    private static IdentityAccessDbContext CreateContext()
    {
        string? connectionString = Environment.GetEnvironmentVariable(
            IdentityAccessDbContextFactory.ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__IdentityAccess to the application principal connection string, or run scripts/db-verify.ps1.");
        }

        DbContextOptionsBuilder<IdentityAccessDbContext> options = new();
        options.UseNpgsql(connectionString);
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(InstitutionId.From("institution-test"));
        return new IdentityAccessDbContext(options.Options, tenantContext);
    }
}
