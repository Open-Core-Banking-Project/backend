using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.IdentityAccess.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreBanking.IdentityAccess.Workers;

/// <summary>
/// Runs the identity maintenance passes: sessions whose expiry has elapsed become
/// <c>Expired</c>, and lapsed security locks are closed. Both passes are idempotent and batched, so
/// a restart or an overlapping replica loses nothing and repeats nothing.
/// Decision: ADR-0008 (session lifecycle), ADR-0004 (tenant isolation).
/// </summary>
public sealed partial class SessionLifecycleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SessionLifecycleOptions _options;
    private readonly ILogger<SessionLifecycleWorker> _logger;

    public SessionLifecycleWorker(
        IServiceScopeFactory scopeFactory,
        SessionLifecycleOptions options,
        ILogger<SessionLifecycleWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                SessionSecurityService service = scope.ServiceProvider.GetRequiredService<SessionSecurityService>();

                int expiredSessions = await DrainAsync(
                    token => service.ExpireLapsedSessionsAsync(_options.BatchSize, token),
                    stoppingToken).ConfigureAwait(false);

                int releasedLocks = await DrainAsync(
                    token => service.ReleaseExpiredLocksAsync(_options.BatchSize, token),
                    stoppingToken).ConfigureAwait(false);

                if (expiredSessions > 0 || releasedLocks > 0)
                {
                    LogPass(_logger, expiredSessions, releasedLocks);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A maintenance pass must never terminate the worker; the next pass retries the backlog.
                LogFailure(_logger, exception);
            }

            await Task.Delay(_options.Interval, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs one pass until it returns fewer rows than a full batch, so a large backlog is cleared
    /// without making one unbounded transaction.
    /// </summary>
    private async Task<int> DrainAsync(Func<CancellationToken, Task<int>> pass, CancellationToken stoppingToken)
    {
        int total = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            int affected = await pass(stoppingToken).ConfigureAwait(false);
            total += affected;
            if (affected < _options.BatchSize)
            {
                return total;
            }
        }

        return total;
    }

    [LoggerMessage(EventId = 2010, Level = LogLevel.Information, Message = "Session maintenance pass: {ExpiredSessions} sessions expired, {ReleasedLocks} security locks closed")]
    private static partial void LogPass(ILogger logger, int expiredSessions, int releasedLocks);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Error, Message = "Session maintenance pass failed; the backlog is retried on the next pass")]
    private static partial void LogFailure(ILogger logger, Exception exception);
}

/// <summary>Schedule for the identity maintenance passes. Configuration section: IdentityLifecycle.</summary>
public sealed class SessionLifecycleOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    public int BatchSize { get; init; } = 200;

    public static SessionLifecycleOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        int intervalSeconds = configuration.GetValue("IdentityLifecycle:SweepIntervalSeconds", 300);
        int batchSize = configuration.GetValue("IdentityLifecycle:SweepBatchSize", 200);
        if (intervalSeconds <= 0)
        {
            throw new InvalidOperationException("IdentityLifecycle:SweepIntervalSeconds must be positive.");
        }

        if (batchSize <= 0)
        {
            throw new InvalidOperationException("IdentityLifecycle:SweepBatchSize must be positive.");
        }

        return new SessionLifecycleOptions
        {
            Interval = TimeSpan.FromSeconds(intervalSeconds),
            BatchSize = batchSize,
        };
    }
}
