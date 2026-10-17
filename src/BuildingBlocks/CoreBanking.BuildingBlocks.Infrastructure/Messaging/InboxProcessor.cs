using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.BuildingBlocks.Infrastructure.Messaging;

/// <summary>What happened to one delivered event.</summary>
public enum InboxResult
{
    /// <summary>The handler ran and its outcome was recorded.</summary>
    Processed = 1,

    /// <summary>The event identity was already recorded, so nothing ran a second time.</summary>
    Duplicate = 2,

    /// <summary>The envelope was rejected before the handler ran and was dead-lettered.</summary>
    Rejected = 3,

    /// <summary>The handler failed; the message stays eligible for a controlled retry.</summary>
    Deferred = 4,
}

/// <summary>
/// Consumer-side deduplication. The event identity is registered in the same unit of work as the
/// consumer outcome, so a repeat delivery is a no-op that returns the recorded outcome, and two
/// concurrent consumers converge on one outcome through the inbox primary key. An unsupported
/// schema version is dead-lettered rather than parsed best-effort.
/// The caller establishes the tenant context from the validated envelope before calling.
/// Decision: ADR-0007.
/// </summary>
public sealed class InboxProcessor
{
    private readonly IIntegrationStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly MessagingMetrics _metrics;
    private readonly int _maximumAttempts;

    public InboxProcessor(
        IIntegrationStore store,
        TimeProvider timeProvider,
        MessagingMetrics? metrics = null,
        int maximumAttempts = RetrySchedule.DefaultMaximumAttempts)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _metrics = metrics ?? MessagingMetrics.Shared;

        if (maximumAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts), "At least one attempt is required.");
        }

        _maximumAttempts = maximumAttempts;
    }

    public async Task<InboxResult> ProcessAsync(
        string consumer,
        IntegrationEventEnvelope envelope,
        int expectedSchemaVersion,
        Func<CancellationToken, Task<string?>> handle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.InstitutionId);

        if (envelope.SchemaVersion != expectedSchemaVersion)
        {
            await RejectAsync(consumer, envelope, expectedSchemaVersion, cancellationToken).ConfigureAwait(false);
            return InboxResult.Rejected;
        }

        InboxRecord? existing = await _store.InboxMessages
            .FindAsync([envelope.InstitutionId, consumer, envelope.EventId], cancellationToken)
            .ConfigureAwait(false);

        if (existing?.ProcessedAt is not null)
        {
            _metrics.InboxDuplicates.Add(1);
            return InboxResult.Duplicate;
        }

        InboxRecord record = existing ?? new InboxRecord
        {
            InstitutionId = envelope.InstitutionId,
            Consumer = consumer,
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            SchemaVersion = envelope.SchemaVersion,
            ReceivedAt = _timeProvider.GetUtcNow(),
        };

        if (existing is null)
        {
            _store.InboxMessages.Add(record);
        }

        long startTicks = Stopwatch.GetTimestamp();

        try
        {
            string? outcome = await handle(cancellationToken).ConfigureAwait(false);

            record.Outcome = outcome;
            record.ProcessedAt = _timeProvider.GetUtcNow();
            record.LastError = null;

            // One SaveChanges covers the inbox row and whatever the handler wrote through the
            // same context, which is what makes the outcome and the dedup record atomic.
            try
            {
                await _store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // Another consumer inserted the same event identity first. The unique inbox key is
                // what stops a second business outcome; this consumer's writes did not commit.
                _store.InboxMessages.Entry(record).State = EntityState.Detached;

                InboxRecord? winner = await _store.InboxMessages
                    .FindAsync([envelope.InstitutionId, consumer, envelope.EventId], cancellationToken)
                    .ConfigureAwait(false);

                if (winner is not null)
                {
                    _metrics.InboxDuplicates.Add(1);
                    return InboxResult.Duplicate;
                }

                throw;
            }

            _metrics.InboxProcessed.Add(1);
            _metrics.ConsumerDuration.Record(Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds);

            return InboxResult.Processed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            record.Attempts++;
            record.LastError = exception.GetType().Name;
            _metrics.InboxFailures.Add(1);

            if (RetrySchedule.IsTerminal(record.Attempts, _maximumAttempts))
            {
                record.Outcome = "dead_lettered";
                record.ProcessedAt = _timeProvider.GetUtcNow();

                _store.DeadLetters.Add(DeadLetterFactory.FromInbox(
                    envelope.InstitutionId,
                    envelope.EventId,
                    envelope.EventType,
                    envelope.Payload,
                    exception.GetType().Name,
                    record.Attempts,
                    _timeProvider.GetUtcNow()));

                _metrics.DeadLetters.Add(1);
            }

            // The record survives so the message stays eligible for a controlled retry, or so the
            // dead letter is visible with its attempt history.
            await _store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return InboxResult.Deferred;
        }
    }

    private async Task RejectAsync(string consumer, IntegrationEventEnvelope envelope, int expectedSchemaVersion, CancellationToken cancellationToken)
    {
        _store.InboxMessages.Add(new InboxRecord
        {
            InstitutionId = envelope.InstitutionId,
            Consumer = consumer,
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            SchemaVersion = envelope.SchemaVersion,
            Outcome = "rejected",
            Attempts = 1,
            LastError = $"unsupported schema version {envelope.SchemaVersion}",
            ReceivedAt = _timeProvider.GetUtcNow(),
            ProcessedAt = _timeProvider.GetUtcNow(),
        });

        _store.DeadLetters.Add(DeadLetterFactory.FromInbox(
            envelope.InstitutionId,
            envelope.EventId,
            envelope.EventType,
            envelope.Payload,
            $"unsupported schema version {envelope.SchemaVersion}; this consumer accepts {expectedSchemaVersion}",
            1,
            _timeProvider.GetUtcNow()));

        _metrics.DeadLetters.Add(1);
        await _store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
