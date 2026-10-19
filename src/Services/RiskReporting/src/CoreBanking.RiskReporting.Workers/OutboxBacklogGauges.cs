using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.RiskReporting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoreBanking.RiskReporting.Workers;

internal static class OutboxBacklogGauges
{
    public static void Register(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        MessagingMetrics.Shared.Meter.CreateObservableGauge(
            "messaging.outbox.unpublished", () => Measure(scopeFactory), description: "Unpublished outbox records.");
    }

    private static long Measure(IServiceScopeFactory scopeFactory)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        using RiskReportingDbContext context = scope.ServiceProvider.GetRequiredService<RiskReportingDbContext>();
        return context.OutboxMessages.IgnoreQueryFilters().LongCount(record => record.Status != OutboxStatus.Published);
    }
}
