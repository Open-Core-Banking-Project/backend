using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Scoped command idempotency. The record is written before the command runs, so a concurrent
/// duplicate never produces a second outcome, and the recorded result is what a replay returns.
/// The record is durable in the owning service's database; Redis is never the only store.
/// Decision: ADR-0007.
/// </summary>
public sealed class IdempotencyService
{
    private readonly IIntegrationStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly MessagingMetrics _metrics;

    public IdempotencyService(IIntegrationStore store, TimeProvider timeProvider, MessagingMetrics? metrics = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _metrics = metrics ?? MessagingMetrics.Shared;
    }

    public async Task<IdempotencyOutcome> ExecuteAsync(
        IdempotencyKey key,
        string requestFingerprint,
        Func<CancellationToken, Task<string?>> command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);

        IdempotencyOutcome? priorOutcome = await ClassifyAsync(key, requestFingerprint, cancellationToken).ConfigureAwait(false);
        if (priorOutcome is not null)
        {
            return priorOutcome;
        }

        IdempotencyRecord record = new()
        {
            InstitutionId = key.InstitutionId,
            Operation = key.Operation,
            Initiator = key.Initiator,
            IdempotencyKey = key.Value,
            RequestFingerprint = requestFingerprint,
            Status = IdempotencyRecordStatus.InProgress,
            CreatedAt = _timeProvider.GetUtcNow(),
        };

        _store.IdempotencyRecords.Add(record);

        try
        {
            await _store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Another caller inserted the same key between the read and the insert.
            _store.IdempotencyRecords.Entry(record).State = EntityState.Detached;

            return await ClassifyAsync(key, requestFingerprint, cancellationToken).ConfigureAwait(false)
                ?? IdempotencyOutcome.InProgress();
        }

        try
        {
            string? result = await command(cancellationToken).ConfigureAwait(false);

            record.Status = IdempotencyRecordStatus.Completed;
            record.Result = result;
            record.CompletedAt = _timeProvider.GetUtcNow();

            await _store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return IdempotencyOutcome.Executed(result);
        }
        catch (Exception)
        {
            // The command did not complete, so the key must not stay claimed by a failed attempt.
            _store.IdempotencyRecords.Remove(record);
            await _store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            throw;
        }
    }

    private async Task<IdempotencyOutcome?> ClassifyAsync(IdempotencyKey key, string requestFingerprint, CancellationToken cancellationToken)
    {
        IdempotencyRecord? existing = await _store.IdempotencyRecords
            .FindAsync([key.InstitutionId, key.Operation, key.Initiator, key.Value], cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(existing.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            _metrics.IdempotencyConflicts.Add(1);
            return IdempotencyOutcome.Conflict();
        }

        if (existing.Status == IdempotencyRecordStatus.Completed)
        {
            _metrics.IdempotencyHits.Add(1);
            return IdempotencyOutcome.Replayed(existing.Result);
        }

        _metrics.IdempotencyInProgress.Add(1);
        return IdempotencyOutcome.InProgress();
    }
}
