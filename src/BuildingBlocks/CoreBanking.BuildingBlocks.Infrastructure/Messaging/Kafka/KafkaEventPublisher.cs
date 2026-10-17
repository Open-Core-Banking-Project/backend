using System;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace CoreBanking.BuildingBlocks.Infrastructure.Messaging.Kafka;

/// <summary>Broker connection settings. The broker product stays replaceable behind this adapter.</summary>
public sealed record KafkaMessagingOptions
{
    public required string BootstrapServers { get; init; }

    public string ClientId { get; init; } = "corebanking";

    /// <summary>Local development only; a production topic inventory is provisioned separately.</summary>
    public bool AllowAutoCreateTopics { get; init; } = true;

    /// <summary>How long a produce request may wait before it counts as a broker failure.</summary>
    public int MessageTimeoutMs { get; init; } = 5000;
}

/// <summary>
/// The Kafka-compatible adapter behind <see cref="IEventPublisher"/>. It waits for the broker
/// acknowledgement required by ADR-0007 before reporting success, and translates a broker failure
/// into <see cref="EventPublicationException"/> so the relay schedules a retry instead of losing
/// the event. Only the relay may use this type; nothing in a request path publishes.
/// Decision: ADR-0007.
/// </summary>
public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly TimeSpan _flushTimeout = TimeSpan.FromSeconds(10);
    private bool _disposed;

    public KafkaEventPublisher(KafkaMessagingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ProducerConfig configuration = new()
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = options.ClientId,
            Acks = Acks.All,
            EnableIdempotence = true,
            AllowAutoCreateTopics = options.AllowAutoCreateTopics,
            MessageSendMaxRetries = 3,
            MessageTimeoutMs = options.MessageTimeoutMs,
            RequestTimeoutMs = options.MessageTimeoutMs,
            // The local broker advertises an IPv4 loopback address; resolving localhost to ::1
            // first would fail to connect.
            BrokerAddressFamily = BrokerAddressFamily.V4,
        };

        _producer = new ProducerBuilder<string, string>(configuration).Build();
    }

    public async Task PublishAsync(string topic, string partitionKey, string envelope, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        ArgumentNullException.ThrowIfNull(envelope);

        Message<string, string> message = new() { Key = partitionKey, Value = envelope };

        try
        {
            await _producer.ProduceAsync(topic, message, cancellationToken).ConfigureAwait(false);
        }
        catch (ProduceException<string, string> exception)
        {
            throw new EventPublicationException(
                $"the broker rejected a message on topic '{topic}': {exception.Error.Code}",
                exception);
        }
        catch (KafkaException exception)
        {
            throw new EventPublicationException(
                $"the broker is unavailable for topic '{topic}': {exception.Error.Code}",
                exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Flush what has been produced before the producer is torn down, so a clean shutdown does
        // not turn an acknowledged message into a lost one.
        _producer.Flush(_flushTimeout);
        _producer.Dispose();
        _disposed = true;
    }
}

/// <summary>Registers the Kafka publisher behind the transport-neutral publisher port.</summary>
public static class KafkaEventPublisherRegistration
{
    public static IServiceCollection AddKafkaEventPublisher(
        this IServiceCollection services,
        KafkaMessagingOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton<IEventPublisher>(_ => new KafkaEventPublisher(options));
        return services;
    }
}
