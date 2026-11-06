using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoreBanking.CoreLedger.Infrastructure.Products;

/// <summary>
/// Observable product and account configuration state for alerting: published versions, approvals
/// waiting to be published, and the outbox backlog. Every measurement is best effort and must never
/// fail an operation. Decision: P1-E04 slice 14.
/// </summary>
public static class ProductAccountGauges
{
    private const string ProductSchema = "product_eligibility.v1";
    private const string AccountOpeningPolicySchema = "institution.account-opening-policy.v1";

    public static void Register(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        ProductAccountMetrics.Shared.Meter.CreateObservableGauge(
            "corebanking.product_account.configurations.published",
            () => Measure(scopeFactory).Published,
            description: "Effective configuration versions currently published across product and currency areas.");
        ProductAccountMetrics.Shared.Meter.CreateObservableGauge(
            "corebanking.product_account.configurations.awaiting_publication",
            () => Measure(scopeFactory).AwaitingPublication,
            description: "Approved configuration versions that are not published yet.");
        ProductAccountMetrics.Shared.Meter.CreateObservableGauge(
            "corebanking.product_account.outbox.pending",
            () => Measure(scopeFactory).PendingOutbox,
            description: "Outbox messages that have not been relayed.");
        ProductAccountMetrics.Shared.Meter.CreateObservableGauge(
            "corebanking.product_account.outbox.oldest_pending_age_seconds",
            () => Measure(scopeFactory).OldestPendingAge.TotalSeconds,
            unit: "s",
            description: "Age of the oldest pending outbox message.");
    }

    private static Snapshot Measure(IServiceScopeFactory scopeFactory)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            using CoreLedgerDbContext context = scope.ServiceProvider.GetRequiredService<CoreLedgerDbContext>();
            TimeProvider clock = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
            long published = context.ConfigurationVersions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .LongCount(record => (record.Schema == ProductSchema
                        || record.Schema == InstitutionCurrencySet.Schema
                        || record.Schema == AccountOpeningPolicySchema)
                    && record.Status == "published");
            long awaiting = context.ConfigurationVersions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .LongCount(record => (record.Schema == ProductSchema
                        || record.Schema == InstitutionCurrencySet.Schema
                        || record.Schema == AccountOpeningPolicySchema)
                    && record.Status == "approved");
            DateTimeOffset[] pending = [.. context.OutboxMessages
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(record => record.Status == OutboxStatus.Pending)
                .Select(record => record.CreatedAt)];
            TimeSpan oldest = pending.Length == 0 ? TimeSpan.Zero : clock.GetUtcNow() - pending.Min();
            return new Snapshot(published, awaiting, pending.LongLength, oldest < TimeSpan.Zero ? TimeSpan.Zero : oldest);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Telemetry collection is best effort and must not fail a configuration operation.
            return default;
        }
    }

    private readonly record struct Snapshot(long Published, long AwaitingPublication, long PendingOutbox, TimeSpan OldestPendingAge);
}
