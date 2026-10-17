using System;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging.Kafka;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Messaging;

/// <summary>
/// The Kafka-compatible adapter against the real local broker: a message must survive a round trip
/// with its partition key and envelope intact, and an unreachable broker must surface as a
/// publication failure the relay can retry rather than a silent loss.
/// Decision: ADR-0007.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KafkaPublisherTests
{
    private const string BootstrapVariableName = "CB_BROKER_BOOTSTRAP_SERVERS";
    private const string Topic = "corebanking.identity.testevents.v1";

    [Fact]
    public async Task A_published_envelope_round_trips_through_the_broker_with_its_partition_key()
    {
        string bootstrap = BootstrapServers();
        string eventId = "event-" + Guid.NewGuid().ToString("N");

        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = "foundation_record_created",
            SchemaVersion = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            InstitutionId = "integration-institution",
            CorrelationId = "correlation-1",
            Producer = "identity-access",
            BusinessReference = "foundation-integration",
            Payload = "{\"foundationRecordId\":\"record-1\"}",
        };

        // Topic auto-creation on first publish can take longer than the default budget, so the
        // round-trip test allows a generous timeout; the unreachable-broker test keeps a short one.
        using (KafkaEventPublisher publisher = new(new KafkaMessagingOptions { BootstrapServers = bootstrap, MessageTimeoutMs = 30000 }))
        {
            await publisher.PublishAsync(Topic, envelope.PartitionKey, EventContract.Serialize(envelope), CancellationToken.None);
        }

        using IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = "corebanking-test-" + Guid.NewGuid().ToString("N"),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();

        consumer.Subscribe(Topic);

        try
        {
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(45));

            while (!deadline.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = consumer.Consume(deadline.Token);
                if (result?.Message?.Value is null || !result.Message.Value.Contains(eventId, StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.Equal(envelope.PartitionKey, result.Message.Key);

                IntegrationEventEnvelope? received = EventContract.Deserialize<IntegrationEventEnvelope>(result.Message.Value);
                Assert.NotNull(received);
                Assert.Equal(eventId, received!.EventId);
                Assert.Equal(envelope.InstitutionId, received.InstitutionId);
                Assert.Equal(envelope.Payload, received.Payload);

                return;
            }

            Assert.Fail("The published envelope did not arrive within the deadline.");
        }
        finally
        {
            consumer.Close();
        }
    }

    [Fact]
    public async Task An_unreachable_broker_surfaces_as_a_publication_failure()
    {
        using KafkaEventPublisher publisher = new(new KafkaMessagingOptions
        {
            BootstrapServers = "127.0.0.1:1",
            MessageTimeoutMs = 500,
        });

        await Assert.ThrowsAsync<EventPublicationException>(
            () => publisher.PublishAsync(Topic, "foundation-1", "{}", CancellationToken.None));
    }

    private static string BootstrapServers()
    {
        string? configured = Environment.GetEnvironmentVariable(BootstrapVariableName);

        return string.IsNullOrWhiteSpace(configured) ? "127.0.0.1:9092" : configured;
    }
}
