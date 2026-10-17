using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.AuditReconciliation.Contracts;
using CoreBanking.AuditReconciliation.Infrastructure.Persistence;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;

namespace CoreBanking.AuditReconciliation.Infrastructure.Foundation;

/// <summary>
/// Consumes Identity Access' foundation event into Audit Reconciliation's independent database.
/// Inbox identity and audit evidence are saved together, making replay unable to create duplicate
/// evidence.
/// </summary>
public sealed class AuditFoundationEventConsumer : IIntegrationEventConsumer
{
    public const string Producer = "identity-access";
    public const string EventFamily = "foundation";
    public const string EventType = "foundation_record_created";
    public const int SchemaVersion = 1;
    public const string ConsumerName = "audit-reconciliation.foundation-audit";
    public static string TopicName => EventContract.TopicFor(Producer, EventFamily, SchemaVersion);

    public string Topic => TopicName;

    private readonly AuditReconciliationDbContext _context;
    private readonly InboxProcessor _inbox;
    private readonly AmbientTenantContext _tenantContext;

    public AuditFoundationEventConsumer(
        AuditReconciliationDbContext context,
        InboxProcessor inbox,
        AmbientTenantContext tenantContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

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
                token =>
                {
                    if (!string.Equals(envelope.Producer, Producer, StringComparison.Ordinal)
                        || !string.Equals(envelope.EventType, EventType, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("The foundation topic contained an unexpected producer or event type.");
                    }

                    FoundationRecordCreatedV1? payload =
                        EventContract.Deserialize<FoundationRecordCreatedV1>(envelope.Payload);
                    if (payload is null
                        || string.IsNullOrWhiteSpace(payload.FoundationRecordId)
                        || string.IsNullOrWhiteSpace(payload.ActorId))
                    {
                        throw new InvalidOperationException("The foundation event payload is missing required identifiers.");
                    }

                    _context.AuditEvidence.Add(new AuditEvidenceRecord
                    {
                        InstitutionId = envelope.InstitutionId,
                        AuditEventId = envelope.EventId,
                        ActorId = payload.ActorId,
                        Action = "foundation_record.created",
                        ResourceType = "foundation_record",
                        ResourceId = payload.FoundationRecordId,
                        Decision = "allowed",
                        Source = envelope.Producer,
                        CorrelationId = envelope.CorrelationId,
                        OccurredAt = envelope.OccurredAt,
                    });

                    return Task.FromResult<string?>("recorded:" + payload.FoundationRecordId);
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _tenantContext.Clear();
        }
    }
}
