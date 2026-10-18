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

/// <summary>
/// Transport evidence for a message that cannot be trusted enough to establish tenant context.
/// Implementations persist it outside tenant-owned inbox/dead-letter tables so no fallback
/// institution is invented.
/// </summary>
public sealed record UntrustedMessage(
    string Transport,
    string Stream,
    int Partition,
    long Position,
    string? MessageKey,
    string Payload,
    string Reason);

/// <summary>Durably quarantines an unreadable transport message before its offset is committed.</summary>
public interface IUntrustedMessageQuarantine
{
    Task QuarantineAsync(UntrustedMessage message, CancellationToken cancellationToken);
}
