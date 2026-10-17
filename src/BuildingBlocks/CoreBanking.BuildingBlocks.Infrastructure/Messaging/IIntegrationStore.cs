using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CoreBanking.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// The integration tables a service owns, exposed so the shared messaging primitives can work
/// against any service's database context without knowing its domain. A service implements this
/// on its own context, which is also what makes the source state and the outbox record commit in
/// one transaction.
/// Decision: ADR-0007.
/// </summary>
public interface IIntegrationStore
{
    DbSet<OutboxRecord> OutboxMessages { get; }

    DbSet<InboxRecord> InboxMessages { get; }

    DbSet<IdempotencyRecord> IdempotencyRecords { get; }

    DbSet<DeadLetterRecord> DeadLetters { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}

/// <summary>The publisher port. Kafka is one adapter; the broker stays replaceable.</summary>
public interface IEventPublisher
{
    Task PublishAsync(string topic, string partitionKey, string envelope, CancellationToken cancellationToken);
}

/// <summary>Raised when the broker cannot accept a message. Publication is retried, never faked.</summary>
public sealed class EventPublicationException : Exception
{
    public EventPublicationException()
    {
    }

    public EventPublicationException(string message)
        : base(message)
    {
    }

    public EventPublicationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
