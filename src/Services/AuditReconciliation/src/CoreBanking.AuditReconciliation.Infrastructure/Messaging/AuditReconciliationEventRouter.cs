using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.AuditReconciliation.Infrastructure.Foundation;
using CoreBanking.AuditReconciliation.Infrastructure.Institutions;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;

namespace CoreBanking.AuditReconciliation.Infrastructure.Messaging;

/// <summary>Routes subscribed event families to their independently idempotent consumers.</summary>
public sealed class AuditReconciliationEventRouter : IIntegrationEventConsumer
{
    private readonly AuditFoundationEventConsumer _foundation;
    private readonly InstitutionProvisioningConsumer _provisioning;

    public AuditReconciliationEventRouter(
        AuditFoundationEventConsumer foundation,
        InstitutionProvisioningConsumer provisioning)
    {
        _foundation = foundation ?? throw new ArgumentNullException(nameof(foundation));
        _provisioning = provisioning ?? throw new ArgumentNullException(nameof(provisioning));
    }

    public string Topic => "multiple";

    public Task<InboxResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken) =>
        envelope.EventType switch
        {
            AuditFoundationEventConsumer.EventType => _foundation.HandleAsync(envelope, cancellationToken),
            InstitutionProvisioningConsumer.SourceEventType => _provisioning.HandleAsync(envelope, cancellationToken),
            _ => throw new InvalidOperationException("No Audit Reconciliation consumer accepts this event type."),
        };
}
