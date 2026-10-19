using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.PaymentsVtu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoreBanking.PaymentsVtu.Workers;

internal static class OutboxBacklogGauges
{
    public static void Register(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        MessagingMetrics.Shared.Meter.CreateObservableGauge(
            "messaging.outbox.unpublished",
            () => Measure(scopeFactory),
            description: "Unpublished outbox records.");
    }

    private static long Measure(IServiceScopeFactory scopeFactory)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        using PaymentsVtuDbContext context = scope.ServiceProvider.GetRequiredService<PaymentsVtuDbContext>();
        return context.OutboxMessages.IgnoreQueryFilters()
            .LongCount(record => record.Status != OutboxStatus.Published);
    }
}
