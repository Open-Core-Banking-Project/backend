using System;
using System.Linq;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoreBanking.CoreLedger.Infrastructure.Institutions;

public static class InstitutionOperationalGauges
{
    public static void Register(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        InstitutionMetrics.Shared.Meter.CreateObservableGauge(
            "corebanking.institution.provisioning.incomplete",
            () => Measure(scopeFactory, timeProvider).Incomplete,
            description: "Institution provisioning runs that have not reached activation readiness.");
        InstitutionMetrics.Shared.Meter.CreateObservableGauge(
            "corebanking.institution.provisioning.failures",
            () => Measure(scopeFactory, timeProvider).Failed,
            description: "Institution provisioning runs requiring owning-service recovery.");
        InstitutionMetrics.Shared.Meter.CreateObservableGauge(
            "corebanking.institution.provisioning.oldest_incomplete_age_seconds",
            () => Measure(scopeFactory, timeProvider).OldestAge.TotalSeconds,
            unit: "s",
            description: "Age of the oldest institution provisioning run that is not activation-ready.");
    }

    private static Snapshot Measure(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            using CoreLedgerDbContext context = scope.ServiceProvider.GetRequiredService<CoreLedgerDbContext>();
            ProvisioningRunRecord[] runs = [.. context.ProvisioningRuns
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(run => run.Status == "provisioning" || run.Status == "provisioning_failed")];
            DateTimeOffset now = timeProvider.GetUtcNow();
            TimeSpan oldest = runs.Length == 0 ? TimeSpan.Zero : now - runs.Min(run => run.StartedAt);
            return new Snapshot(runs.LongLength, runs.LongCount(run => run.Status == "provisioning_failed"), oldest);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Telemetry collection is best effort and must not fail an institution operation.
            return default;
        }
    }

    private readonly record struct Snapshot(long Incomplete, long Failed, TimeSpan OldestAge);
}
