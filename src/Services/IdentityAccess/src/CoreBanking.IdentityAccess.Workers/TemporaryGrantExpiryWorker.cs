using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.IdentityAccess.Infrastructure.Access;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreBanking.IdentityAccess.Workers;

/// <summary>
/// Closes lapsed temporary grants and advances the affected entitlement versions, so a consumer
/// holding an older version denies instead of keeping a lapsed permission alive. The pass ignores
/// the tenant filter and carries each row's own institution, so one replica services every tenant.
/// Decision: ADR-0009 and P1-E02-D10.
/// </summary>
public sealed partial class TemporaryGrantExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TemporaryGrantExpiryOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TemporaryGrantExpiryWorker> _logger;

    public TemporaryGrantExpiryWorker(
        IServiceScopeFactory scopeFactory,
        TemporaryGrantExpiryOptions options,
        TimeProvider timeProvider,
        ILogger<TemporaryGrantExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                GrantAdministrationService service = scope.ServiceProvider.GetRequiredService<GrantAdministrationService>();

                int expired = await service
                    .ExpireDueTemporaryGrantsAsync(_timeProvider.GetUtcNow(), TemporaryGrantExpiryOptions.CorrelationId, stoppingToken)
                    .ConfigureAwait(false);

                if (expired > 0)
                {
                    LogPass(_logger, expired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A maintenance pass must never terminate the worker; the next pass retries.
                LogFailure(_logger, exception);
            }

            await Task.Delay(_options.Interval, stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 2020, Level = LogLevel.Information, Message = "Temporary grant expiry pass closed {ExpiredGrants} grants")]
    private static partial void LogPass(ILogger logger, int expiredGrants);

    [LoggerMessage(EventId = 2021, Level = LogLevel.Error, Message = "Temporary grant expiry pass failed; the backlog is retried on the next pass")]
    private static partial void LogFailure(ILogger logger, Exception exception);
}

/// <summary>Schedule for the temporary-grant expiry sweep. Configuration section: IdentityGrants.</summary>
public sealed class TemporaryGrantExpiryOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(1);

    public static string CorrelationId => "identity.temporary_grant.expiry_sweep";

    public static TemporaryGrantExpiryOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        int intervalSeconds = configuration.GetValue("IdentityGrants:ExpirySweepSeconds", 60);
        if (intervalSeconds <= 0)
        {
            throw new InvalidOperationException("IdentityGrants:ExpirySweepSeconds must be positive.");
        }

        return new TemporaryGrantExpiryOptions
        {
            Interval = TimeSpan.FromSeconds(intervalSeconds),
        };
    }
}
