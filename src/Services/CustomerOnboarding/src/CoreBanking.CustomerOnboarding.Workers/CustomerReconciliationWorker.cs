using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CustomerOnboarding.Application.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreBanking.CustomerOnboarding.Workers;

/// <summary>
/// Periodically reports customer-evidence integrity findings: merges awaiting consumers, applied merges without an
/// alias, approval-less sensitive transitions, KYC requirements without a verification and aliases whose survivor
/// is missing. The pass never repairs state. Decision: P1-E03-D14.
/// </summary>
public sealed partial class CustomerReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CustomerReconciliationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CustomerReconciliationWorker> _logger;

    public CustomerReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        CustomerReconciliationOptions options,
        TimeProvider timeProvider,
        ILogger<CustomerReconciliationWorker> logger)
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
    public async Task<CustomerReconciliationReport> RunPassAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ICustomerReconciliationService service = scope.ServiceProvider.GetRequiredService<ICustomerReconciliationService>();

        CustomerReconciliationReport report = await service.ReconcileAsync(now, cancellationToken).ConfigureAwait(false);

        foreach (KeyValuePair<string, int> finding in report.FindingsByCode)
        {
            CustomerOnboardingMetrics.RecordFindings(finding.Key, finding.Value);
        }

        CustomerOnboardingMetrics.RecordPass(report.IsClean);

        if (!report.IsClean)
        {
            LogFindings(_logger, report.TotalFindings);
        }

        return report;
    }

    [LoggerMessage(EventId = 2500, Level = LogLevel.Warning, Message = "Customer reconciliation found {TotalFindings} integrity findings; operators must review each code.")]
    private static partial void LogFindings(ILogger logger, int totalFindings);

    [LoggerMessage(EventId = 2501, Level = LogLevel.Error, Message = "Customer reconciliation pass failed; the next pass retries")]
    private static partial void LogFailure(ILogger logger, Exception exception);
}

/// <summary>Schedule for the customer reconciliation pass. Configuration section: CustomerReconciliation.</summary>
public sealed class CustomerReconciliationOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    public static CustomerReconciliationOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        int intervalSeconds = configuration.GetValue("CustomerReconciliation:IntervalSeconds", 3600);
        if (intervalSeconds <= 0)
        {
            throw new InvalidOperationException("CustomerReconciliation:IntervalSeconds must be positive.");
        }

        return new CustomerReconciliationOptions
        {
            Interval = TimeSpan.FromSeconds(intervalSeconds),
        };
    }
}
