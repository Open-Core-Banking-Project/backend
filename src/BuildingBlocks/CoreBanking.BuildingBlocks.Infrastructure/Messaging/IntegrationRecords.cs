using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Lifecycle of a durable outbox record. <c>ManualRepair</c> is a terminal state an operator must
/// resolve; a record is never silently dropped.
/// Decision: ADR-0007.
/// </summary>
public enum OutboxStatus
{
    Pending = 1,
    Published = 2,
    ManualRepair = 3,
}

/// <summary>
/// One event awaiting publication, written in the same local transaction as its source state.
/// Table: <c>integration.outbox_messages</c>. The technical shape is shared; the table, its
/// migration and its retention belong to the owning service.
/// Decision: ADR-0007.
/// </summary>
public sealed class OutboxRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string MessageId { get; set; }

    /// <summary>The event identity. Publication retries never create a second event identity.</summary>
    public required string EventId { get; set; }

    public required string EventType { get; set; }

    public required int SchemaVersion { get; set; }

    public required string Topic { get; set; }

    public required string BusinessReference { get; set; }

    public required string CorrelationId { get; set; }

    public string? CausationId { get; set; }

    public string? TraceParent { get; set; }

    public string? TraceState { get; set; }

    public required string Producer { get; set; }

    /// <summary>The serialized envelope, exactly as it is published.</summary>
    public required string Envelope { get; set; }

    public OutboxStatus Status { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>
/// The recorded processing of one event by one consumer, written in the same transaction as the
/// consumer's outcome. A repeat delivery is detected here.
/// Table: <c>integration.inbox_messages</c>. Decision: ADR-0007.
/// </summary>
public sealed class InboxRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string Consumer { get; set; }

    public required string EventId { get; set; }

    public required string EventType { get; set; }

    public required int SchemaVersion { get; set; }

    /// <summary>The serialized outcome, or <c>null</c> when the handler produced nothing.</summary>
    public string? Outcome { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
}

/// <summary>State of one idempotent command. Decision: ADR-0007.</summary>
public enum IdempotencyRecordStatus
{
    InProgress = 1,
    Completed = 2,
}

/// <summary>
/// The durable record of one scoped idempotency key. Redis is never the only store for an
/// authoritative operation. Table: <c>integration.idempotency_records</c>. Decision: ADR-0007.
/// </summary>
public sealed class IdempotencyRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string Operation { get; set; }

    public required string Initiator { get; set; }

    public required string IdempotencyKey { get; set; }

    public required string RequestFingerprint { get; set; }

    public IdempotencyRecordStatus Status { get; set; }

    public string? Result { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Where a dead letter came from. Decision: ADR-0007.</summary>
public enum DeadLetterSource
{
    Outbox = 1,
    Inbox = 2,
}

/// <summary>
/// A message that can no longer be retried automatically. Visible to operators, replayable by a
/// documented procedure that keeps the original event identity.
/// Table: <c>integration.dead_letters</c>. Decision: ADR-0007.
/// </summary>
public sealed class DeadLetterRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string DeadLetterId { get; set; }

    public DeadLetterSource Source { get; set; }

    public required string EventId { get; set; }

    public string? EventType { get; set; }

    public string? Payload { get; set; }

    public required string Reason { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? RepairedAt { get; set; }
}
