using System;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CustomerOnboarding.Contracts;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Customers;

/// <summary>
/// Writes one customer event and its outbox row in the caller's transaction, using the shared envelope and
/// topic rules. Decision: ADR-0007, P1-E03-D11.
/// </summary>
internal static class CustomerEventWriter
{
    public static void Add(
        CustomerOnboardingDbContext context,
        InstitutionId institutionId,
        string businessReference,
        string eventType,
        object payload,
        string correlationId,
        DateTimeOffset now,
        string? eventFamily = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(institutionId);

        string family = string.IsNullOrWhiteSpace(eventFamily) ? CustomerContractV1.EventFamily : eventFamily.Trim();
        string eventId = Guid.NewGuid().ToString("N");
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = eventType,
            SchemaVersion = CustomerContractV1.SchemaVersion,
            OccurredAt = now,
            InstitutionId = institutionId.Value,
            CorrelationId = correlationId,
            Producer = CustomerContractV1.Producer,
            BusinessReference = businessReference,
            Payload = EventContract.Serialize(payload),
        };

        context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = institutionId.Value,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = eventType,
            SchemaVersion = CustomerContractV1.SchemaVersion,
            Topic = EventContract.TopicFor(CustomerContractV1.Producer, family, CustomerContractV1.SchemaVersion),
            BusinessReference = businessReference,
            CorrelationId = correlationId,
            Producer = CustomerContractV1.Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });
    }
}
