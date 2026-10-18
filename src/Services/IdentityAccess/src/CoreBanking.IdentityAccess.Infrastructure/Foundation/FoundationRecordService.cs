using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Platform;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IdentityAccess.Infrastructure.Foundation;

/// <summary>The result of creating one synthetic foundation record.</summary>
public sealed record FoundationRecordResult(string FoundationRecordId, string EventId, bool Replayed);

/// <summary>
/// The epic 04 reference flow. The foundation record and its outbox record are written in one
/// transaction, so the event either describes a committed record or does not exist. A scoped
/// idempotency key makes a repeated request return the original outcome instead of a second
/// record, and a materially different request under the same key is rejected.
/// Publishing is not this type's job: the relay does that after the commit.
/// Decision: ADR-0007, epic 04 reference flow.
/// </summary>
public sealed class FoundationRecordService
{
    public const string Producer = "identity-access";
    public const string EventFamily = "foundation";
    public const string EventType = "foundation_record_created";
    public const int SchemaVersion = 1;
    public const string Operation = "foundation.record.create";

    private readonly IdentityAccessDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly TimeProvider _timeProvider;

    public FoundationRecordService(IdentityAccessDbContext context, IdempotencyService idempotency, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<FoundationRecordResult> CreateAsync(
        InstitutionId institutionId,
        ActorId actorId,
        string idempotencyKey,
        string description,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        IdempotencyKey key = IdempotencyKey.Create(institutionId.Value, Operation, actorId.Value, idempotencyKey);
        string fingerprint = IdempotencyKey.Fingerprint(description);

        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            key,
            fingerprint,
            async token =>
            {
                FoundationRecord record = new()
                {
                    InstitutionId = institutionId.Value,
                    FoundationRecordId = Guid.NewGuid().ToString("N"),
                    Description = description,
                    CreatedBy = actorId.Value,
                    CreatedAt = _timeProvider.GetUtcNow(),
                };

                string eventId = Guid.NewGuid().ToString("N");
                IntegrationEventEnvelope envelope = new()
                {
                    EventId = eventId,
                    EventType = EventType,
                    SchemaVersion = SchemaVersion,
                    OccurredAt = record.CreatedAt,
                    InstitutionId = institutionId.Value,
                    CorrelationId = correlationId,
                    Producer = Producer,
                    BusinessReference = record.FoundationRecordId,
                    Payload = EventContract.Serialize(new FoundationRecordCreatedV1(record.FoundationRecordId, actorId.Value)),
                };

                await using var transaction = await _context.BeginTransactionAsync(token).ConfigureAwait(false);

                _context.FoundationRecords.Add(record);
                _context.OutboxMessages.Add(new OutboxRecord
                {
                    InstitutionId = institutionId.Value,
                    MessageId = Guid.NewGuid().ToString("N"),
                    EventId = eventId,
                    EventType = EventType,
                    SchemaVersion = SchemaVersion,
                    Topic = EventContract.TopicFor(Producer, EventFamily, SchemaVersion),
                    BusinessReference = record.FoundationRecordId,
                    CorrelationId = correlationId,
                    TraceParent = Activity.Current?.Id,
                    TraceState = Activity.Current?.TraceStateString,
                    Producer = Producer,
                    Envelope = EventContract.Serialize(envelope),
                    Status = OutboxStatus.Pending,
                    Attempts = 0,
                    NextAttemptAt = _timeProvider.GetUtcNow(),
                    CreatedAt = record.CreatedAt,
                });

                await _context.SaveChangesAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);

                return new CreatedOutcome(record.FoundationRecordId, eventId).Encode();
            },
            cancellationToken).ConfigureAwait(false);

        return outcome.Kind switch
        {
            IdempotencyOutcomeKind.Executed => Created(outcome, replayed: false),
            IdempotencyOutcomeKind.Replayed => Created(outcome, replayed: true),
            IdempotencyOutcomeKind.Conflict => throw new IdempotencyConflictException(
                "the idempotency key was already used with a materially different request"),
            _ => throw new IdempotencyInProgressException(
                "an equivalent request with this idempotency key is still in progress"),
        };
    }

    private static FoundationRecordResult Created(IdempotencyOutcome outcome, bool replayed)
    {
        CreatedOutcome created = CreatedOutcome.Decode(outcome.Result);
        return new FoundationRecordResult(created.FoundationRecordId, created.EventId, replayed);
    }

    /// <summary>The stable, serialized outcome that a replay returns unchanged.</summary>
    private sealed record CreatedOutcome(string FoundationRecordId, string EventId)
    {
        public string Encode() => FoundationRecordId + "|" + EventId;

        public static CreatedOutcome Decode(string? value)
        {
            string[] parts = (value ?? string.Empty).Split('|');
            if (parts.Length != 2)
            {
                throw new InvalidOperationException("The recorded idempotency outcome is not a foundation record outcome.");
            }

            return new CreatedOutcome(parts[0], parts[1]);
        }
    }
}

/// <summary>The same idempotency key was used with a materially different request.</summary>
public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string message)
        : base(message)
    {
    }

    public IdempotencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>An equivalent request with the same key has not finished yet.</summary>
public sealed class IdempotencyInProgressException : Exception
{
    public IdempotencyInProgressException(string message)
        : base(message)
    {
    }

    public IdempotencyInProgressException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
