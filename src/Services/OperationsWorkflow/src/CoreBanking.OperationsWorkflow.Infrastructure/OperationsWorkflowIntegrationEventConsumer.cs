using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.OperationsWorkflow.Contracts;
using CoreBanking.OperationsWorkflow.Infrastructure.Approvals;
using CoreBanking.OperationsWorkflow.Infrastructure.Institutions;

namespace CoreBanking.OperationsWorkflow.Infrastructure;

public sealed class OperationsWorkflowIntegrationEventConsumer : IIntegrationEventConsumer
{
    private readonly InstitutionProvisioningConsumer _institutions;
    private readonly ApprovalRequestedConsumer _approvals;
    private readonly ApprovalApplicationResultConsumer _results;

    public OperationsWorkflowIntegrationEventConsumer(
        InstitutionProvisioningConsumer institutions,
        ApprovalRequestedConsumer approvals,
        ApprovalApplicationResultConsumer results)
    {
        _institutions = institutions ?? throw new ArgumentNullException(nameof(institutions));
        _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
        _results = results ?? throw new ArgumentNullException(nameof(results));
    }

    public string Topic => throw new NotSupportedException("The routing consumer subscribes to multiple configured topics.");

    public Task<InboxResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (string.Equals(envelope.EventType, ApprovalRequestedConsumer.EventType, StringComparison.Ordinal))
        {
            return _approvals.HandleAsync(envelope, cancellationToken);
        }

        return string.Equals(envelope.EventType, ApprovalApplicationResultV1Codes.EventType, StringComparison.Ordinal)
            ? _results.HandleAsync(envelope, cancellationToken)
            : _institutions.HandleAsync(envelope, cancellationToken);
    }
}
