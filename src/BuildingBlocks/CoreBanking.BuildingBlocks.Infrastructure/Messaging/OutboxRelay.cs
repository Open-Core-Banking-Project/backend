using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Diagnostics;
using CoreBanking.BuildingBlocks.Application.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Tuning for the outbox relay. Defaults are the Phase 0 policy from ADR-0007.</summary>
public sealed record OutboxRelayOptions
{
    public int BatchSize { get; init; } = 20;

    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromMinutes(5);

    public int MaximumAttempts { get; init; } = RetrySchedule.DefaultMaximumAttempts;
}

/// <summary>The result of one relay pass, which is also the operational evidence.</summary>
public sealed record OutboxRelayResult(int Claimed, int Published, int Failed, int DeadLettered)
{
    public static OutboxRelayResult Empty { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// Claims pending outbox records with <c>FOR UPDATE SKIP LOCKED</c>, publishes them, and records
/// the outcome in the same transaction. A record becomes <c>published</c> only after the broker
/// acknowledged it; a failure schedules a retry and, when attempts are exhausted, moves the
/// record to <c>manual_repair</c> and raises a dead letter. Retry never recreates the event.
/// This type is the only place in the repository that publishes. Decision: ADR-0007.
/// </summary>
public sealed class OutboxRelay
{
    private readonly IIntegrationStore _store;
    private readonly IEventPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly OutboxRelayOptions _options;
    private readonly MessagingMetrics _metrics;

    public OutboxRelay(
        IIntegrationStore store,
        IEventPublisher publisher,
        TimeProvider timeProvider,
        OutboxRelayOptions? options = null,
        MessagingMetrics? metrics = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? new OutboxRelayOptions();
        _metrics = metrics ?? MessagingMetrics.Shared;
    }

    public async Task<OutboxRelayResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        // The relay is a system process that publishes for every institution, so it deliberately
        // reads across the tenant boundary. No request path may do this.
        FormattableString claim = $"""
            SELECT * FROM integration.outbox_messages
            WHERE status = 'Pending' AND next_attempt_at <= {now}
            ORDER BY next_attempt_at
            LIMIT {_options.BatchSize}
            FOR UPDATE SKIP LOCKED
            """;

        List<string> claimed = await _store.OutboxMessages
            .FromSql(claim)
            .IgnoreQueryFilters()
            .Select(record => record.MessageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (claimed.Count == 0)
        {
            return OutboxRelayResult.Empty;
        }

        int published = 0;
        int failed = 0;
        int deadLettered = 0;

        foreach (string messageId in claimed)
        {
            OutboxRecord record = await _store.OutboxMessages
                .IgnoreQueryFilters()
                .SingleAsync(candidate => candidate.MessageId == messageId, cancellationToken)
                .ConfigureAwait(false);

            using Activity? activity = StartPublishActivity(record);
            try
            {
                await _publisher.PublishAsync(record.Topic, record.BusinessReference, record.Envelope, cancellationToken).ConfigureAwait(false);

                record.Status = OutboxStatus.Published;
                record.PublishedAt = _timeProvider.GetUtcNow();
                record.LastError = null;
                published++;
                _metrics.OutboxPublished.Add(1);
            }
            catch (EventPublicationException exception)
            {
                activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                failed++;
                record.Attempts++;
                record.LastError = exception.Message;
                _metrics.OutboxPublishFailures.Add(1);
                _metrics.OutboxRetries.Add(1);

                if (RetrySchedule.IsTerminal(record.Attempts, _options.MaximumAttempts))
                {
                    record.Status = OutboxStatus.ManualRepair;
                    _store.DeadLetters.Add(DeadLetterFactory.FromOutbox(record, _timeProvider.GetUtcNow()));
                    deadLettered++;
                    _metrics.DeadLetters.Add(1);
                }
                else
                {
                    double jitter = Random.Shared.NextDouble() * 0.25d;
                    record.NextAttemptAt = _timeProvider.GetUtcNow()
                        + RetrySchedule.DelayFor(record.Attempts, _options.BaseRetryDelay, _options.MaximumRetryDelay, jitter);
                }
            }
        }

        await _store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new OutboxRelayResult(claimed.Count, published, failed, deadLettered);
    }

    private static Activity? StartPublishActivity(OutboxRecord record)
    {
        ActivityContext parentContext = default;
        bool hasParent = ActivityContext.TryParse(
            record.TraceParent,
            record.TraceState,
            isRemote: true,
            out parentContext);

        Activity? activity = hasParent
            ? CoreBankingActivitySource.Instance.StartActivity("messaging.publish", ActivityKind.Producer, parentContext)
            : CoreBankingActivitySource.Instance.StartActivity("messaging.publish", ActivityKind.Producer);

        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", record.Topic);
        activity?.SetTag("messaging.operation.type", "publish");
        activity?.SetTag("messaging.event.type", record.EventType);
        activity?.SetTag("correlation.id", record.CorrelationId);
        return activity;
    }

    /// <summary>Unpublished count and oldest age, the two gauges the epic requires.</summary>
    public async Task<(long Unpublished, TimeSpan OldestAge)> BacklogAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<DateTimeOffset> createdAt = await _store.OutboxMessages
            .IgnoreQueryFilters()
            .Where(record => record.Status != OutboxStatus.Published)
            .Select(record => record.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        TimeSpan oldestAge = createdAt.Count == 0 ? TimeSpan.Zero : now - createdAt.Min();

        return (createdAt.Count, oldestAge);
    }
}

/// <summary>Builds the durable evidence for a message that needs a human.</summary>
public static class DeadLetterFactory
{
    public static DeadLetterRecord FromOutbox(OutboxRecord record, DateTimeOffset now) => new()
    {
        InstitutionId = record.InstitutionId,
        DeadLetterId = Guid.NewGuid().ToString("N"),
        Source = DeadLetterSource.Outbox,
        EventId = record.EventId,
        EventType = record.EventType,
        Payload = record.Envelope,
        Reason = record.LastError ?? "publication failed",
        Attempts = record.Attempts,
        CreatedAt = now,
    };

    public static DeadLetterRecord FromInbox(
        string institutionId,
        string eventId,
        string? eventType,
        string? payload,
        string reason,
        int attempts,
        DateTimeOffset now) => new()
        {
            InstitutionId = institutionId,
            DeadLetterId = Guid.NewGuid().ToString("N"),
            Source = DeadLetterSource.Inbox,
            EventId = eventId,
            EventType = eventType,
            Payload = payload,
            Reason = reason,
            Attempts = attempts,
            CreatedAt = now,
        };
}

/// <summary>
/// The messaging metrics required by epic 04. Labels are bounded identifiers only: event type,
/// topic and outcome. No payload, correlation id or personal data is recorded.
/// Decision: ADR-0007, ADR-0010.
/// </summary>
public sealed class MessagingMetrics
{
    public const string MeterName = "CoreBanking.Messaging";

    private MessagingMetrics(Meter meter)
    {
        Meter = meter;
        OutboxPublished = meter.CreateCounter<long>("messaging.outbox.published");
        OutboxPublishFailures = meter.CreateCounter<long>("messaging.outbox.publish_failures");
        OutboxRetries = meter.CreateCounter<long>("messaging.outbox.retries");
        InboxProcessed = meter.CreateCounter<long>("messaging.inbox.processed");
        InboxDuplicates = meter.CreateCounter<long>("messaging.inbox.duplicates");
        InboxFailures = meter.CreateCounter<long>("messaging.inbox.failures");
        DeadLetters = meter.CreateCounter<long>("messaging.dead_letters");
        IdempotencyHits = meter.CreateCounter<long>("messaging.idempotency.hits");
        IdempotencyConflicts = meter.CreateCounter<long>("messaging.idempotency.conflicts");
        IdempotencyInProgress = meter.CreateCounter<long>("messaging.idempotency.in_progress");
        ConsumerDuration = meter.CreateHistogram<double>("messaging.consumer.duration", unit: "ms");
    }

    public static MessagingMetrics Shared { get; } = new(new Meter(MeterName));

    public Meter Meter { get; }

    public Counter<long> OutboxPublished { get; }

    public Counter<long> OutboxPublishFailures { get; }

    public Counter<long> OutboxRetries { get; }

    public Counter<long> InboxProcessed { get; }

    public Counter<long> InboxDuplicates { get; }

    public Counter<long> InboxFailures { get; }

    public Counter<long> DeadLetters { get; }

    public Counter<long> IdempotencyHits { get; }

    public Counter<long> IdempotencyConflicts { get; }

    public Counter<long> IdempotencyInProgress { get; }

    public Histogram<double> ConsumerDuration { get; }
}
