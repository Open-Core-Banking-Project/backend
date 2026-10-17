using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreBanking.IdentityAccess.Workers;

/// <summary>
/// Runs the outbox relay on a schedule. This is the only process that publishes Identity and
/// Access events, which is what keeps every publication behind a committed local transaction.
/// Decision: ADR-0007.
/// </summary>
public sealed partial class OutboxRelayWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxRelayWorker> _logger;

    public OutboxRelayWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxRelayWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan idle = TimeSpan.FromSeconds(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                OutboxRelay relay = scope.ServiceProvider.GetRequiredService<OutboxRelay>();

                OutboxRelayResult result = await relay.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (result.Claimed > 0)
                {
                    LogPass(_logger, result.Claimed, result.Published, result.Failed, result.DeadLettered);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A relay pass must never terminate the worker; the next pass retries the backlog.
                LogFailure(_logger, exception);
            }

            await Task.Delay(idle, stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Outbox pass: claimed {Claimed}, published {Published}, failed {Failed}, dead-lettered {DeadLettered}")]
    private static partial void LogPass(ILogger logger, int claimed, int published, int failed, int deadLettered);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "Outbox pass failed; the backlog is retried on the next pass")]
    private static partial void LogFailure(ILogger logger, Exception exception);
}
