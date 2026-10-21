using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Infrastructure.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Products;

namespace CoreBanking.CoreLedger.Infrastructure;

public sealed class CoreLedgerIntegrationEventConsumer : IIntegrationEventConsumer
{
    private readonly InstitutionBoundaryAcknowledgementConsumer _institutions;
    private readonly SavingsEligibilityApprovalDecidedConsumer _approvals;

    public CoreLedgerIntegrationEventConsumer(
        InstitutionBoundaryAcknowledgementConsumer institutions,
        SavingsEligibilityApprovalDecidedConsumer approvals)
    {
        _institutions = institutions ?? throw new ArgumentNullException(nameof(institutions));
        _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
    }

    public string Topic => throw new NotSupportedException("The routing consumer subscribes to multiple configured topics.");

    public Task<InboxResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken) =>
        string.Equals(envelope.EventType, SavingsEligibilityApprovalDecidedConsumer.EventType, StringComparison.Ordinal)
            ? _approvals.HandleAsync(envelope, cancellationToken)
            : _institutions.HandleAsync(envelope, cancellationToken);
}
