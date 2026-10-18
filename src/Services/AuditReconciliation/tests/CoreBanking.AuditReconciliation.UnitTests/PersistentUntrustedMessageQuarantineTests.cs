using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.AuditReconciliation.Infrastructure.Persistence;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.AuditReconciliation.UnitTests;

[Trait("Category", "Database")]
public sealed class PersistentUntrustedMessageQuarantineTests : IAsyncLifetime
{
    private const string ConnectionStringVariableName = "ConnectionStrings__AuditReconciliation";
    private readonly long _position = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static string ConnectionString => Environment.GetEnvironmentVariable(ConnectionStringVariableName)
        ?? throw new InvalidOperationException(
            $"Set {ConnectionStringVariableName} to the application principal connection string, or run scripts/db-verify.ps1.");

    [Fact]
    public async Task Malformed_message_is_durable_and_repeated_quarantine_is_idempotent()
    {
        UntrustedMessage message = new(
            "kafka",
            "corebanking.identity-access.foundation.v1",
            7,
            _position,
            "message-key",
            "{not-json",
            "JsonException");

        await using (AuditReconciliationDbContext context = CreateContext())
        {
            PersistentUntrustedMessageQuarantine quarantine = new(context, TimeProvider.System);
            await quarantine.QuarantineAsync(message, CancellationToken.None);
            await quarantine.QuarantineAsync(message, CancellationToken.None);
        }

        await using AuditReconciliationDbContext verification = CreateContext();
        UntrustedMessageQuarantineRecord record = Assert.Single(verification.UntrustedMessageQuarantine
            .Where(candidate => candidate.Position == _position)
            .ToList());

        Assert.Equal(message.Stream, record.Stream);
        Assert.Equal(message.Payload, record.Payload);
        Assert.Equal(message.Reason, record.Reason);
        Assert.Null(record.RepairedAt);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using AuditReconciliationDbContext context = CreateContext();
        await context.UntrustedMessageQuarantine
            .Where(record => record.Position == _position)
            .ExecuteDeleteAsync();
    }

    private static AuditReconciliationDbContext CreateContext()
    {
        DbContextOptionsBuilder<AuditReconciliationDbContext> options = new();
        options.UseNpgsql(ConnectionString);
        return new AuditReconciliationDbContext(options.Options, new UnestablishedTenantContext());
    }

    private sealed class UnestablishedTenantContext : ITenantContext
    {
        public InstitutionId? CurrentInstitution => null;

        public void Establish(InstitutionId institutionId) =>
            throw new NotSupportedException("Quarantine persistence must not establish a fallback institution.");

        public void Clear()
        {
        }
    }
}
