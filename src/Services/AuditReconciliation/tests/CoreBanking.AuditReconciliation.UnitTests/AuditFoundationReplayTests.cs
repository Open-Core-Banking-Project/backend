using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.AuditReconciliation.Contracts;
using CoreBanking.AuditReconciliation.Infrastructure.Foundation;
using CoreBanking.AuditReconciliation.Infrastructure.Persistence;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.AuditReconciliation.UnitTests;

[Trait("Category", "Database")]
public sealed class AuditFoundationReplayTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__AuditReconciliation";

    [Fact]
    public async Task Replaying_a_committed_event_records_one_inbox_outcome_and_one_audit_event()
    {
        string eventId = Guid.NewGuid().ToString("N");
        string institutionId = "institution-" + Guid.NewGuid().ToString("N");
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = AuditFoundationEventConsumer.EventType,
            SchemaVersion = AuditFoundationEventConsumer.SchemaVersion,
            OccurredAt = DateTimeOffset.UtcNow,
            InstitutionId = institutionId,
            CorrelationId = Guid.NewGuid().ToString("N"),
            Producer = AuditFoundationEventConsumer.Producer,
            BusinessReference = "foundation-" + Guid.NewGuid().ToString("N"),
            Payload = EventContract.Serialize(new FoundationRecordCreatedV1("record-test", "actor-test")),
        };

        InboxResult first = await ConsumeAsync(envelope);
        InboxResult replay = await ConsumeAsync(envelope);

        Assert.Equal(InboxResult.Processed, first);
        Assert.Equal(InboxResult.Duplicate, replay);

        await using AuditReconciliationDbContext verification = CreateContext();
        Assert.Equal(1, await verification.AuditEvidence
            .IgnoreQueryFilters()
            .CountAsync(record => record.AuditEventId == eventId));
        Assert.Equal(1, await verification.InboxMessages
            .IgnoreQueryFilters()
            .CountAsync(record => record.EventId == eventId));
    }

    private static async Task<InboxResult> ConsumeAsync(IntegrationEventEnvelope envelope)
    {
        AmbientTenantContext tenantContext = new();
        await using AuditReconciliationDbContext context = CreateContext(tenantContext);
        InboxProcessor inbox = new(context, TimeProvider.System);
        AuditFoundationEventConsumer consumer = new(context, inbox, tenantContext);
        return await consumer.HandleAsync(envelope, CancellationToken.None);
    }

    private static AuditReconciliationDbContext CreateContext(AmbientTenantContext? tenantContext = null)
    {
        string? connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringVariableName} to the application principal connection string, or run scripts/db-verify.ps1.");
        }

        DbContextOptionsBuilder<AuditReconciliationDbContext> options = new();
        options.UseNpgsql(connectionString);
        return new AuditReconciliationDbContext(options.Options, tenantContext ?? new AmbientTenantContext());
    }
}
