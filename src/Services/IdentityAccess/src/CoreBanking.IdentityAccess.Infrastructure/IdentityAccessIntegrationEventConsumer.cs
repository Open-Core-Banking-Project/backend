using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Infrastructure.Access;
using CoreBanking.IdentityAccess.Infrastructure.Institutions;

namespace CoreBanking.IdentityAccess.Infrastructure;

/// <summary>Routes the worker's configured integration topics to their bounded handlers.</summary>
public sealed class IdentityAccessIntegrationEventConsumer : IIntegrationEventConsumer
{
    private readonly InstitutionProvisioningConsumer _institutions;
    private readonly RoleApprovalDecidedConsumer _roleApprovals;

    public IdentityAccessIntegrationEventConsumer(
        InstitutionProvisioningConsumer institutions,
        RoleApprovalDecidedConsumer roleApprovals)
    {
        _institutions = institutions ?? throw new ArgumentNullException(nameof(institutions));
        _roleApprovals = roleApprovals ?? throw new ArgumentNullException(nameof(roleApprovals));
    }

    public string Topic => throw new NotSupportedException("The routing consumer subscribes to multiple configured topics.");

    public Task<InboxResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return string.Equals(envelope.EventType, RolePublicationApprovalV1.DecidedEventType, StringComparison.Ordinal)
            ? _roleApprovals.HandleAsync(envelope, cancellationToken)
            : _institutions.HandleAsync(envelope, cancellationToken);
    }
}
