using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Messaging;

[Trait("Category", "Unit")]
public sealed class MessagingMetricsTests
{
    [Fact]
    public void Required_counter_and_histogram_instruments_are_observable()
    {
        List<string> observed = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (string.Equals(instrument.Meter.Name, MessagingMetrics.MeterName, StringComparison.Ordinal))
            {
                observed.Add(instrument.Name);
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => { });
        listener.SetMeasurementEventCallback<double>((_, _, _, _) => { });
        listener.Start();

        MessagingMetrics metrics = MessagingMetrics.Shared;
        metrics.OutboxPublished.Add(1);
        metrics.OutboxPublishFailures.Add(1);
        metrics.OutboxRetries.Add(1);
        metrics.InboxProcessed.Add(1);
        metrics.InboxDuplicates.Add(1);
        metrics.InboxFailures.Add(1);
        metrics.DeadLetters.Add(1);
        metrics.IdempotencyHits.Add(1);
        metrics.IdempotencyConflicts.Add(1);
        metrics.IdempotencyInProgress.Add(1);
        metrics.ConsumerDuration.Record(1);

        Assert.Contains("messaging.outbox.published", observed);
        Assert.Contains("messaging.outbox.publish_failures", observed);
        Assert.Contains("messaging.outbox.retries", observed);
        Assert.Contains("messaging.inbox.processed", observed);
        Assert.Contains("messaging.inbox.duplicates", observed);
        Assert.Contains("messaging.inbox.failures", observed);
        Assert.Contains("messaging.dead_letters", observed);
        Assert.Contains("messaging.idempotency.hits", observed);
        Assert.Contains("messaging.idempotency.conflicts", observed);
        Assert.Contains("messaging.idempotency.in_progress", observed);
        Assert.Contains("messaging.consumer.duration", observed);
    }
}
