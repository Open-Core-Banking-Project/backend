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
/// Periodically reports identity state that needs an operator: grants without a version, lapsed
/// grants the sweep has not closed, expired service credentials still active and unusable step-up
/// evidence. The pass never repairs state; repair stays an explicit, audited action.
/// Decision: ADR-0010; slice 14.
/// </summary>
public sealed partial class IdentityReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IdentityReconciliationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IdentityReconciliationWorker> _logger;

    public IdentityReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        IdentityReconciliationOptions options,
        TimeProvider timeProvider,
        ILogger<IdentityReconciliationWorker> logger)
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
                await RunPassAsync(_timeProvider.GetUtcNow(), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogFailure(_logger, exception);
            }

            await Task.Delay(_options.Interval, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>One pass, exposed so an operator command and the tests can run it directly.</summary>
    public async Task<EntitlementReconciliationFindings> RunPassAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        EntitlementReconciliationService service = scope.ServiceProvider.GetRequiredService<EntitlementReconciliationService>();

        EntitlementReconciliationFindings findings = await service.ReconcileAsync(now, cancellationToken).ConfigureAwait(false);

        IdentityAccessMetrics.GrantHoldersWithoutVersion.Add(findings.GrantHoldersWithoutVersion);
        IdentityAccessMetrics.ExpiredGrantsNotClosed.Add(findings.ExpiredGrantsNotClosed);
        IdentityAccessMetrics.ExpiredServiceClientsActive.Add(findings.ExpiredServiceClientsActive);
        IdentityAccessMetrics.StaleStepUpEvidence.Add(findings.StaleStepUpEvidence);

        if (!findings.IsClean)
        {
            LogFindings(
                _logger,
                findings.GrantHoldersWithoutVersion,
                findings.ExpiredGrantsNotClosed,
                findings.ExpiredServiceClientsActive,
                findings.StaleStepUpEvidence);
        }

        return findings;
    }

    [LoggerMessage(EventId = 2030, Level = LogLevel.Warning, Message = "Identity reconciliation findings: {GrantHoldersWithoutVersion} grant holders without version, {ExpiredGrantsNotClosed} expired grants not closed, {ExpiredServiceClientsActive} expired service clients active, {StaleStepUpEvidence} stale step-up evidence records")]
    private static partial void LogFindings(
        ILogger logger,
        long grantHoldersWithoutVersion,
        long expiredGrantsNotClosed,
        long expiredServiceClientsActive,
        long staleStepUpEvidence);

    [LoggerMessage(EventId = 2031, Level = LogLevel.Error, Message = "Identity reconciliation pass failed; the next pass retries")]
    private static partial void LogFailure(ILogger logger, Exception exception);
}

/// <summary>Schedule for the identity reconciliation pass. Configuration section: IdentityReconciliation.</summary>
public sealed class IdentityReconciliationOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    public static IdentityReconciliationOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        int intervalSeconds = configuration.GetValue("IdentityReconciliation:IntervalSeconds", 3600);
        if (intervalSeconds <= 0)
        {
            throw new InvalidOperationException("IdentityReconciliation:IntervalSeconds must be positive.");
        }

        return new IdentityReconciliationOptions
        {
            Interval = TimeSpan.FromSeconds(intervalSeconds),
        };
    }
}
