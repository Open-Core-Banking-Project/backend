using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoreBanking.CoreLedger.Workers;

internal static class OutboxBacklogGauges
{
    public static void Register(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        MessagingMetrics.Shared.Meter.CreateObservableGauge(
            "messaging.outbox.unpublished",
            () => Measure(scopeFactory, static (count, _) => count),
            description: "Unpublished outbox records.");
        MessagingMetrics.Shared.Meter.CreateObservableGauge(
            "messaging.outbox.oldest_age_seconds",
            () => Measure(scopeFactory, static (_, age) => age.TotalSeconds),
            description: "Age of the oldest unpublished outbox record.");
    }

    private static double Measure(IServiceScopeFactory scopeFactory, Func<long, TimeSpan, double> select)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        using CoreLedgerDbContext context = scope.ServiceProvider.GetRequiredService<CoreLedgerDbContext>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset[] pending = [.. context.OutboxMessages
            .IgnoreQueryFilters()
            .Where(record => record.Status != OutboxStatus.Published)
            .Select(record => record.CreatedAt)];
        TimeSpan age = pending.Length == 0 ? TimeSpan.Zero : now - pending.Min();
        return select(pending.Length, age);
    }
}
