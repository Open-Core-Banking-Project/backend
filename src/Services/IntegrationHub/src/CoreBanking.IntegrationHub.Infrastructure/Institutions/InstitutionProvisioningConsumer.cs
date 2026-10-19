using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IntegrationHub.Contracts;
using CoreBanking.IntegrationHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IntegrationHub.Infrastructure.Institutions;

public sealed class InstitutionProvisioningConsumer : IIntegrationEventConsumer
{
    public const string SourceProducer = "core-ledger";
    public const string EventFamily = "institution";
    public const string SourceEventType = "institution_provisioning_started";
    public const string ActivationEventType = "institution_activated";
    public const string SuspensionEventType = "institution_suspended";
    public const string AcknowledgementEventType = "institution_boundary_initialised";
    public const string FailureEventType = "institution_boundary_initialisation_failed";
    public const string ServiceId = "integration-hub";
    public const string ConsumerName = "integration-hub.institution-provisioning";
    public const int SchemaVersion = 1;

    private readonly IntegrationHubDbContext _context;
    private readonly InboxProcessor _inbox;
    private readonly AmbientTenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public InstitutionProvisioningConsumer(
        IntegrationHubDbContext context,
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

    public async Task<InboxResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _tenantContext.Clear();
        _tenantContext.Establish(InstitutionId.From(envelope.InstitutionId));
        try
        {
            return await _inbox.ProcessAsync(
                ConsumerName, envelope, SchemaVersion, token => InitialiseAsync(envelope, token), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _tenantContext.Clear();
        }
    }

    private async Task<string?> InitialiseAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!string.Equals(envelope.Producer, SourceProducer, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The institution topic contained an unexpected producer or event type.");
        }

        if (string.Equals(envelope.EventType, ActivationEventType, StringComparison.Ordinal))
        {
            return await ActivateAsync(envelope, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(envelope.EventType, SuspensionEventType, StringComparison.Ordinal))
        {
            return await SuspendAsync(envelope, cancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(envelope.EventType, SourceEventType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The institution topic contained an unexpected producer or event type.");
        }

        InstitutionProvisioningStartedV1 payload = EventContract.Deserialize<InstitutionProvisioningStartedV1>(envelope.Payload)
            ?? throw new InvalidOperationException("The provisioning event payload is missing.");
        if (string.IsNullOrWhiteSpace(payload.InstitutionId)
            || !string.Equals(payload.InstitutionId, envelope.InstitutionId, StringComparison.Ordinal)
            || payload.ProvisioningPlanVersion < 1)
        {
            throw new InvalidOperationException("The provisioning payload does not match the trusted envelope.");
        }

        InstitutionBoundaryRecord? existing = await _context.InstitutionBoundaries.SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ProvisioningPlanVersion != payload.ProvisioningPlanVersion)
            {
                AddAcknowledgement(envelope, payload, FailureEventType, "provisioning_plan_conflict");
                return "failed:provisioning_plan_conflict";
            }

            return "already_initialised:" + existing.InstitutionId;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        _context.InstitutionBoundaries.Add(new InstitutionBoundaryRecord
        {
            InstitutionId = payload.InstitutionId,
            ProvisioningPlanVersion = payload.ProvisioningPlanVersion,
            Status = "provisioning",
            SourceEventId = envelope.EventId,
            InitialisedAt = now,
        });
        AddAcknowledgement(envelope, payload, AcknowledgementEventType, null);
        return "initialised:" + payload.InstitutionId;
    }

    private async Task<string?> ActivateAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        InstitutionActivatedV1 payload = EventContract.Deserialize<InstitutionActivatedV1>(envelope.Payload)
            ?? throw new InvalidOperationException("The activation event payload is missing.");
        if (string.IsNullOrWhiteSpace(payload.InstitutionId)
            || !string.Equals(payload.InstitutionId, envelope.InstitutionId, StringComparison.Ordinal)
            || payload.ProvisioningPlanVersion < 1
            || string.IsNullOrWhiteSpace(payload.ProvisioningRunId)
            || string.IsNullOrWhiteSpace(payload.ActivatedByActorId))
        {
            throw new InvalidOperationException("The activation payload does not match the trusted envelope.");
        }

        InstitutionBoundaryRecord boundary = await _context.InstitutionBoundaries.SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The institution boundary must exist before activation.");
        if (boundary.ProvisioningPlanVersion != payload.ProvisioningPlanVersion)
        {
            throw new InvalidOperationException("The activation event does not match the local provisioning plan.");
        }

        if (string.Equals(boundary.Status, "active", StringComparison.Ordinal))
        {
            return "already_active:" + boundary.InstitutionId;
        }

        if (!(string.Equals(boundary.Status, "provisioning", StringComparison.Ordinal)
                || string.Equals(boundary.Status, "suspended", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The institution boundary cannot transition to active from its current state.");
        }

        boundary.Status = "active";
        return "activated:" + boundary.InstitutionId;
    }

    private async Task<string?> SuspendAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        InstitutionSuspendedV1 payload = EventContract.Deserialize<InstitutionSuspendedV1>(envelope.Payload)
            ?? throw new InvalidOperationException("The suspension event payload is missing.");
        if (string.IsNullOrWhiteSpace(payload.InstitutionId)
            || !string.Equals(payload.InstitutionId, envelope.InstitutionId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(payload.SuspendedByActorId)
            || string.IsNullOrWhiteSpace(payload.Reason))
        {
            throw new InvalidOperationException("The suspension payload does not match the trusted envelope.");
        }

        InstitutionBoundaryRecord boundary = await _context.InstitutionBoundaries.SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The institution boundary must exist before suspension.");
        if (string.Equals(boundary.Status, "suspended", StringComparison.Ordinal))
        {
            return "already_suspended:" + boundary.InstitutionId;
        }

        if (!string.Equals(boundary.Status, "active", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The institution boundary cannot transition to suspended from its current state.");
        }

        boundary.Status = "suspended";
        return "suspended:" + boundary.InstitutionId;
    }

    private void AddAcknowledgement(
        IntegrationEventEnvelope source,
        InstitutionProvisioningStartedV1 payload,
        string eventType,
        string? failureCode)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string eventId = Guid.NewGuid().ToString("N");
        string payloadJson = failureCode is null
            ? EventContract.Serialize(new InstitutionBoundaryInitialisedV1(
                payload.InstitutionId, ServiceId, payload.ProvisioningPlanVersion, source.EventId))
            : EventContract.Serialize(new InstitutionBoundaryInitialisationFailedV1(
                payload.InstitutionId, ServiceId, payload.ProvisioningPlanVersion, source.EventId, failureCode));
        IntegrationEventEnvelope acknowledgement = new()
        {
            EventId = eventId,
            EventType = eventType,
            SchemaVersion = SchemaVersion,
            OccurredAt = now,
            InstitutionId = payload.InstitutionId,
            CorrelationId = source.CorrelationId,
            CausationId = source.EventId,
            Producer = ServiceId,
            BusinessReference = payload.InstitutionId,
            Payload = payloadJson,
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = payload.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = eventType,
            SchemaVersion = SchemaVersion,
            Topic = EventContract.TopicFor(ServiceId, EventFamily, SchemaVersion),
            BusinessReference = payload.InstitutionId,
            CorrelationId = source.CorrelationId,
            CausationId = source.EventId,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            Producer = ServiceId,
            Envelope = EventContract.Serialize(acknowledgement),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });
    }
}
