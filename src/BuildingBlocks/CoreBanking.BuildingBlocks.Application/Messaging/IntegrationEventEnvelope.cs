using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoreBanking.BuildingBlocks.Application.Messaging;

/// <summary>
/// The transport-neutral event envelope. Field names, ordering and meaning are fixed by
/// ADR-0007; broker headers are an implementation detail of the shared messaging primitive.
/// The payload is a versioned contract type serialized as UTF-8 JSON with camel-case names.
/// Decision: docs/adr/ADR-0007-event-envelope-broker-outbox-inbox-and-replay.md
/// </summary>
public sealed record IntegrationEventEnvelope
{
    public required string EventId { get; init; }

    /// <summary>Past-tense event name, for example <c>foundation_record_created</c>.</summary>
    public required string EventType { get; init; }

    /// <summary>Major schema version. A breaking change is a new major on a new topic.</summary>
    public required int SchemaVersion { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string InstitutionId { get; init; }

    public required string CorrelationId { get; init; }

    public string? CausationId { get; init; }

    /// <summary>The producing service, never a host or instance name.</summary>
    public required string Producer { get; init; }

    /// <summary>The business aggregate reference, which is also the partition key.</summary>
    public required string BusinessReference { get; init; }

    /// <summary>The versioned payload as UTF-8 JSON. Never contains secrets.</summary>
    public required string Payload { get; init; }

    /// <summary>The partition key: per-aggregate ordering, never global ordering.</summary>
    public string PartitionKey => BusinessReference;
}

/// <summary>Serialization and topic rules shared by every producer and consumer.</summary>
public static class EventContract
{
    /// <summary>
    /// The only serializer configuration used for envelopes. Timestamps are ISO 8601 and names
    /// are camel case, so a non-.NET consumer can be added without a contract change.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// <c>corebanking.&lt;service&gt;.&lt;event-family&gt;.v&lt;major&gt;</c>, as fixed by ADR-0007.
    /// </summary>
    public static string TopicFor(string service, string eventFamily, int schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventFamily);

        if (schemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "A schema version starts at 1.");
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "corebanking.{0}.{1}.v{2}",
            service.Trim().ToLowerInvariant(),
            eventFamily.Trim().ToLowerInvariant(),
            schemaVersion);
    }

    /// <summary>Parses the major version back out of a topic name, or returns <c>null</c>.</summary>
    public static int? SchemaVersionOf(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return null;
        }

        int marker = topic.LastIndexOf(".v", StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        return int.TryParse(topic.AsSpan(marker + 2), NumberStyles.None, CultureInfo.InvariantCulture, out int version)
            ? version
            : null;
    }

    public static string Serialize<TEnvelope>(TEnvelope envelope) =>
        JsonSerializer.Serialize(envelope, SerializerOptions);

    public static TEnvelope? Deserialize<TEnvelope>(string json) =>
        JsonSerializer.Deserialize<TEnvelope>(json, SerializerOptions);
}
