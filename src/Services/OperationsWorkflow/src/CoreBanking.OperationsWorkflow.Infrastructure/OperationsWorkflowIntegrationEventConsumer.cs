using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.OperationsWorkflow.Infrastructure.Approvals;
using CoreBanking.OperationsWorkflow.Infrastructure.Institutions;

namespace CoreBanking.OperationsWorkflow.Infrastructure;

public sealed class OperationsWorkflowIntegrationEventConsumer : IIntegrationEventConsumer
{
    private readonly InstitutionProvisioningConsumer _institutions;
    private readonly SavingsEligibilityApprovalRequestedConsumer _approvals;

    public OperationsWorkflowIntegrationEventConsumer(
        InstitutionProvisioningConsumer institutions,
        SavingsEligibilityApprovalRequestedConsumer approvals)
    {
        _institutions = institutions ?? throw new ArgumentNullException(nameof(institutions));
        _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
    }

    public string Topic => throw new NotSupportedException("The routing consumer subscribes to multiple configured topics.");

    public Task<InboxResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken) =>
        string.Equals(envelope.EventType, SavingsEligibilityApprovalRequestedConsumer.EventType, StringComparison.Ordinal)
            ? _approvals.HandleAsync(envelope, cancellationToken)
            : _institutions.HandleAsync(envelope, cancellationToken);
}
