using System;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Application.Authorisation;
using CoreBanking.IdentityAccess.Infrastructure.Authorisation;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Persistence;

[Trait("Category", "Database")]
public sealed class PersistentAuthorisationAuditWriterTests
{
    [Fact]
    public async Task Writes_permission_evidence_to_the_identity_access_audit_table()
    {
        await using IdentityAccessDbContext context = CreateContext();
        PersistentAuthorisationAuditWriter writer = new(context);
        string auditEventId = Guid.NewGuid().ToString("N");
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;

        await writer.WriteAsync(new AuthorisationAuditEntry
        {
            AuditEventId = auditEventId,
            OccurredAt = occurredAt,
            ActorId = ActorId.From("actor-test"),
            ActorType = ActorType.Human,
            InstitutionId = InstitutionId.From("institution-test"),
            Permission = "platform.foundation.execute",
            Action = "authorise",
            Decision = "denied",
            Reason = "permission.missing",
            Source = "identity.foundation.execute",
            CorrelationId = Guid.NewGuid().ToString("N"),
            PolicyVersion = "test",
        }, default);

        var evidence = await context.AuthorisationAuditEvents
            .SingleAsync(record => record.AuditEventId == auditEventId);

        Assert.Equal("actor-test", evidence.ActorId);
        Assert.Equal("denied", evidence.Decision);
        Assert.Equal(occurredAt, evidence.OccurredAt);
    }

    [Fact]
    public async Task Application_principal_cannot_delete_authorisation_evidence()
    {
        await using IdentityAccessDbContext context = CreateContext();
        PersistentAuthorisationAuditWriter writer = new(context);
        string auditEventId = Guid.NewGuid().ToString("N");

        await writer.WriteAsync(new AuthorisationAuditEntry
        {
            AuditEventId = auditEventId,
            OccurredAt = DateTimeOffset.UtcNow,
            Permission = "platform.foundation.execute",
            Action = "authorise",
            Decision = "denied",
            Reason = "permission.missing",
            Source = "identity.foundation.execute",
            CorrelationId = Guid.NewGuid().ToString("N"),
        }, default);

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        var evidence = await context.AuthorisationAuditEvents
            .SingleAsync(record => record.AuditEventId == auditEventId);
        context.AuthorisationAuditEvents.Remove(evidence);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Application_principal_cannot_update_authorisation_evidence()
    {
        await using IdentityAccessDbContext context = CreateContext();
        PersistentAuthorisationAuditWriter writer = new(context);
        string auditEventId = Guid.NewGuid().ToString("N");

        await writer.WriteAsync(new AuthorisationAuditEntry
        {
            AuditEventId = auditEventId,
            OccurredAt = DateTimeOffset.UtcNow,
            Permission = "platform.foundation.execute",
            Action = "authorise",
            Decision = "denied",
            Reason = "permission.missing",
            Source = "identity.foundation.execute",
            CorrelationId = Guid.NewGuid().ToString("N"),
        }, default);

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        var evidence = await context.AuthorisationAuditEvents
            .SingleAsync(record => record.AuditEventId == auditEventId);
        evidence.Decision = "allowed";

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        await transaction.RollbackAsync();
    }

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
        return new IdentityAccessDbContext(options.Options, new AmbientTenantContext());
    }
}
