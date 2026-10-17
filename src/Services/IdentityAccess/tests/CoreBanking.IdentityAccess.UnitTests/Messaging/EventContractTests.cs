using System;
using System.Text.Json;
using CoreBanking.BuildingBlocks.Application.Messaging;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Messaging;

/// <summary>
/// The envelope contract itself: topic naming, version parsing, serialization and the retry
/// schedule. These rules are shared by every producer and consumer, so they are tested without a
/// broker or a database. Decision: ADR-0007.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EventContractTests
{
    [Fact]
    public void A_topic_carries_service_family_and_major_version()
    {
        Assert.Equal("corebanking.identity.foundation.v1", EventContract.TopicFor("Identity", "Foundation", 1));
    }

    [Fact]
    public void A_topic_version_can_be_read_back()
    {
        string topic = EventContract.TopicFor("core-ledger", "posting", 2);

        Assert.Equal(2, EventContract.SchemaVersionOf(topic));
        Assert.Null(EventContract.SchemaVersionOf("corebanking.identity.foundation"));
        Assert.Null(EventContract.SchemaVersionOf("corebanking.identity.foundation.vx"));
    }

    [Fact]
    public void A_schema_version_starts_at_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EventContract.TopicFor("identity", "foundation", 0));
    }

    [Fact]
    public void The_envelope_serializes_as_camel_case_json_with_iso_timestamps()
    {
        IntegrationEventEnvelope envelope = new()
        {
            EventId = "event-1",
            EventType = "foundation_record_created",
            SchemaVersion = 1,
            OccurredAt = new DateTimeOffset(2026, 10, 17, 6, 0, 0, TimeSpan.Zero),
            InstitutionId = "institution-1",
            CorrelationId = "correlation-1",
            Producer = "identity-access",
            BusinessReference = "foundation-1",
            Payload = "{}",
        };

        string json = EventContract.Serialize(envelope);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal("event-1", document.RootElement.GetProperty("eventId").GetString());
        Assert.Equal("2026-10-17T06:00:00+00:00", document.RootElement.GetProperty("occurredAt").GetString());
        // A null causation id is omitted rather than sent as null.
        Assert.False(document.RootElement.TryGetProperty("causationId", out _));
    }

    [Fact]
    public void The_partition_key_is_the_business_reference()
    {
        IntegrationEventEnvelope envelope = new()
        {
            EventId = "event-1",
            EventType = "foundation_record_created",
            SchemaVersion = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            InstitutionId = "institution-1",
            CorrelationId = "correlation-1",
            Producer = "identity-access",
            BusinessReference = "account-42",
            Payload = "{}",
        };

        Assert.Equal("account-42", envelope.PartitionKey);
    }

    [Fact]
    public void An_idempotency_key_is_scoped_and_its_fingerprint_is_stable()
    {
        IdempotencyKey key = IdempotencyKey.Create("institution-1", "foundation.create", "actor-1", "key-1");

        Assert.Equal("institution-1/foundation.create/actor-1/key-1", key.ToString());
        Assert.Equal(IdempotencyKey.Fingerprint("{\"a\":1}"), IdempotencyKey.Fingerprint("{\"a\":1}"));
        Assert.NotEqual(IdempotencyKey.Fingerprint("{\"a\":1}"), IdempotencyKey.Fingerprint("{\"a\":2}"));
    }

    [Fact]
    public void The_retry_delay_grows_then_stops_at_the_maximum()
    {
        TimeSpan baseDelay = TimeSpan.FromSeconds(2);
        TimeSpan maximum = TimeSpan.FromSeconds(30);

        Assert.Equal(TimeSpan.FromSeconds(2), RetrySchedule.DelayFor(1, baseDelay, maximum));
        Assert.Equal(TimeSpan.FromSeconds(4), RetrySchedule.DelayFor(2, baseDelay, maximum));
        Assert.Equal(TimeSpan.FromSeconds(8), RetrySchedule.DelayFor(3, baseDelay, maximum));
        Assert.Equal(maximum, RetrySchedule.DelayFor(10, baseDelay, maximum));
    }

    [Fact]
    public void Jitter_never_increases_the_delay()
    {
        TimeSpan withoutJitter = RetrySchedule.DelayFor(4, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1));
        TimeSpan withJitter = RetrySchedule.DelayFor(4, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1), 0.25d);

        Assert.True(withJitter < withoutJitter);
    }

    [Fact]
    public void A_record_is_terminal_only_when_its_attempts_are_exhausted()
    {
        Assert.False(RetrySchedule.IsTerminal(9, 10));
        Assert.True(RetrySchedule.IsTerminal(10, 10));
    }
}
