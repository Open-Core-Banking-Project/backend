using System;
using System.Linq;
using CoreBanking.AuditReconciliation.Infrastructure.Persistence;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoreBanking.AuditReconciliation.Workers;

/// <summary>Registers consumer backlog measurements without exporting message contents.</summary>
internal static class MessagingBacklogGauges
{
    public static void Register(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        MessagingMetrics.Shared.Meter.CreateObservableGauge(
            "messaging.inbox.unprocessed",
            () => Measure(scopeFactory, timeProvider).Unprocessed,
            description: "Inbox records that have not reached a durable outcome.");

        MessagingMetrics.Shared.Meter.CreateObservableGauge(
            "messaging.dead_letters.unrepaired",
            () => Measure(scopeFactory, timeProvider).DeadLetters,
            description: "Dead letters awaiting operator repair.");

        MessagingMetrics.Shared.Meter.CreateObservableGauge(
            "messaging.dead_letters.oldest_age_seconds",
            () => Measure(scopeFactory, timeProvider).OldestDeadLetterAge.TotalSeconds,
            description: "Age of the oldest dead letter awaiting operator repair.");
    }

    private static BacklogMeasurement Measure(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        using AuditReconciliationDbContext context = scope.ServiceProvider.GetRequiredService<AuditReconciliationDbContext>();

        long unprocessed = context.InboxMessages
            .IgnoreQueryFilters()
            .LongCount(record => record.ProcessedAt == null);

        DateTimeOffset[] deadLetters = [.. context.DeadLetters
            .IgnoreQueryFilters()
            .Where(record => record.RepairedAt == null)
            .Select(record => record.CreatedAt)];

        TimeSpan oldestAge = deadLetters.Length == 0
            ? TimeSpan.Zero
            : timeProvider.GetUtcNow() - deadLetters.Min();

        return new BacklogMeasurement(unprocessed, deadLetters.LongLength, oldestAge);
    }

    private sealed record BacklogMeasurement(long Unprocessed, long DeadLetters, TimeSpan OldestDeadLetterAge);
}
