using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoreBanking.CustomerOnboarding.Workers;

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
        using CustomerOnboardingDbContext context =
            scope.ServiceProvider.GetRequiredService<CustomerOnboardingDbContext>();
        return context.OutboxMessages
            .IgnoreQueryFilters()
            .LongCount(record => record.Status != OutboxStatus.Published);
    }
}
