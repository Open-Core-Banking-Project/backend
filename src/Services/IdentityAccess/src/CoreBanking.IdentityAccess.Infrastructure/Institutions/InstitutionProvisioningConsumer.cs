using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Platform;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IdentityAccess.Infrastructure.Institutions;

/// <summary>
/// Idempotently creates Identity and Access' local institution boundary and acknowledges it
/// through the service-owned outbox. The boundary remains non-active until a later activation
/// event is consumed.
/// </summary>
public sealed class InstitutionProvisioningConsumer : IIntegrationEventConsumer
{
    public const string SourceProducer = "core-ledger";
    public const string EventFamily = "institution";
    public const string SourceEventType = "institution_provisioning_started";
    public const string AcknowledgementEventType = "institution_boundary_initialised";
    public const string FailureEventType = "institution_boundary_initialisation_failed";
    public const string ServiceId = "identity-access";
    public const int SchemaVersion = 1;
    public const string ConsumerName = "identity-access.institution-provisioning";

    private readonly IdentityAccessDbContext _context;
    private readonly InboxProcessor _inbox;
    private readonly AmbientTenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public InstitutionProvisioningConsumer(
        IdentityAccessDbContext context,
        InboxProcessor inbox,
        AmbientTenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public static string TopicName => EventContract.TopicFor(SourceProducer, EventFamily, SchemaVersion);

    public string Topic => TopicName;

    public async Task<InboxResult> HandleAsync(
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _tenantContext.Clear();
        _tenantContext.Establish(InstitutionId.From(envelope.InstitutionId));

        try
        {
            return await _inbox.ProcessAsync(
                ConsumerName,
                envelope,
                SchemaVersion,
                token => InitialiseBoundaryAsync(envelope, token),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _tenantContext.Clear();
        }
    }

    private async Task<string?> InitialiseBoundaryAsync(
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ValidateEnvelope(envelope);
        InstitutionProvisioningStartedV1 payload =
            EventContract.Deserialize<InstitutionProvisioningStartedV1>(envelope.Payload)
            ?? throw new InvalidOperationException("The provisioning event payload is missing.");

        if (string.IsNullOrWhiteSpace(payload.InstitutionId)
            || !string.Equals(payload.InstitutionId, envelope.InstitutionId, StringComparison.Ordinal)
            || payload.ProvisioningPlanVersion < 1)
        {
            throw new InvalidOperationException("The provisioning payload does not match the trusted envelope.");
        }

        InstitutionBoundaryRecord? existing = await _context.InstitutionBoundaries
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ProvisioningPlanVersion != payload.ProvisioningPlanVersion)
            {
                AddFailureAcknowledgement(envelope, payload, "provisioning_plan_conflict");
                return "failed:provisioning_plan_conflict";
            }

            return "already_initialised:" + existing.InstitutionId;
        }

        DateTimeOffset initialisedAt = _timeProvider.GetUtcNow();
        _context.InstitutionBoundaries.Add(new InstitutionBoundaryRecord
        {
            InstitutionId = payload.InstitutionId,
            ProvisioningPlanVersion = payload.ProvisioningPlanVersion,
            Status = "provisioning",
            SourceEventId = envelope.EventId,
            InitialisedAt = initialisedAt,
        });

        string acknowledgementEventId = Guid.NewGuid().ToString("N");
        IntegrationEventEnvelope acknowledgement = new()
        {
            EventId = acknowledgementEventId,
            EventType = AcknowledgementEventType,
            SchemaVersion = SchemaVersion,
            OccurredAt = initialisedAt,
            InstitutionId = payload.InstitutionId,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.EventId,
            Producer = ServiceId,
            BusinessReference = payload.InstitutionId,
            Payload = EventContract.Serialize(new InstitutionBoundaryInitialisedV1(
                payload.InstitutionId,
                ServiceId,
                payload.ProvisioningPlanVersion,
                envelope.EventId)),
        };

        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = payload.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = acknowledgementEventId,
            EventType = AcknowledgementEventType,
            SchemaVersion = SchemaVersion,
            Topic = EventContract.TopicFor(ServiceId, EventFamily, SchemaVersion),
            BusinessReference = payload.InstitutionId,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.EventId,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            Producer = ServiceId,
            Envelope = EventContract.Serialize(acknowledgement),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = initialisedAt,
            CreatedAt = initialisedAt,
        });

        return "initialised:" + payload.InstitutionId;
    }

    private void AddFailureAcknowledgement(
        IntegrationEventEnvelope source,
        InstitutionProvisioningStartedV1 payload,
        string failureCode)
    {
        DateTimeOffset failedAt = _timeProvider.GetUtcNow();
        string failureEventId = Guid.NewGuid().ToString("N");
        IntegrationEventEnvelope failure = new()
        {
            EventId = failureEventId,
            EventType = FailureEventType,
            SchemaVersion = SchemaVersion,
            OccurredAt = failedAt,
            InstitutionId = payload.InstitutionId,
            CorrelationId = source.CorrelationId,
            CausationId = source.EventId,
            Producer = ServiceId,
            BusinessReference = payload.InstitutionId,
            Payload = EventContract.Serialize(new InstitutionBoundaryInitialisationFailedV1(
                payload.InstitutionId,
                ServiceId,
                payload.ProvisioningPlanVersion,
                source.EventId,
                failureCode)),
        };

        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = payload.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = failureEventId,
            EventType = FailureEventType,
            SchemaVersion = SchemaVersion,
            Topic = EventContract.TopicFor(ServiceId, EventFamily, SchemaVersion),
            BusinessReference = payload.InstitutionId,
            CorrelationId = source.CorrelationId,
            CausationId = source.EventId,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            Producer = ServiceId,
            Envelope = EventContract.Serialize(failure),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = failedAt,
            CreatedAt = failedAt,
        });
    }

    private static void ValidateEnvelope(IntegrationEventEnvelope envelope)
    {
        if (!string.Equals(envelope.Producer, SourceProducer, StringComparison.Ordinal)
            || !string.Equals(envelope.EventType, SourceEventType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The institution topic contained an unexpected producer or event type.");
        }
    }
}
