using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;

namespace CoreBanking.BuildingBlocks.Infrastructure.Messaging;

/// <summary>A service-owned consumer for one versioned integration-event topic.</summary>
public interface IIntegrationEventConsumer
{
    string Topic { get; }

    Task<InboxResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken);
}
