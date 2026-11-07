using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Products;

/// <summary>
/// Reconciles the product and currency configuration areas of one institution: publications against
/// their versions, approval evidence, effective periods and the outbox backlog. Decision: P1-E04 slice 14.
/// </summary>
public sealed class ConfigurationReconciliationService : IConfigurationReconciliationService
{
    private const string ProductSchema = "product_eligibility.v1";

    private readonly CoreLedgerDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public ConfigurationReconciliationService(
        CoreLedgerDbContext context,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ConfigurationReconciliationReport> ReconcileAsync(CancellationToken cancellationToken)
    {
        if (_tenantContext.CurrentInstitution is null)
        {
            throw new InvalidOperationException("Configuration reconciliation requires an institution context.");
        }

        Dictionary<string, string> areas = (await _context.ConfigurationSets
            .AsNoTracking()
            .Select(record => new { record.ConfigurationSetId, record.Area })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(record => record.ConfigurationSetId, record => record.Area, StringComparer.Ordinal);

        ConfigurationVersionFacts[] versions = (await _context.ConfigurationVersions
            .AsNoTracking()
            .Where(record => record.Schema == ProductSchema || record.Schema == InstitutionCurrencySet.Schema)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .Select(record => new ConfigurationVersionFacts(
                record.VersionId,
                record.ConfigurationSetId,
                areas.TryGetValue(record.ConfigurationSetId, out string? area) ? area : string.Empty,
                record.Schema,
                record.Status,
                record.ContentHash,
                record.ApprovalReference,
                record.ApprovedByActorId,
                record.EffectiveFrom,
                record.EffectiveTo))
            .ToArray();

        ConfigurationPublicationFacts[] publications = (await _context.ConfigurationPublications
            .AsNoTracking()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .Select(record => new ConfigurationPublicationFacts(
                record.PublicationId,
                record.ConfigurationSetId,
                record.VersionId,
                record.ContentHash,
                record.EffectiveFrom,
                record.EffectiveTo))
            .ToArray();

        DateTimeOffset[] pending = (await _context.OutboxMessages
            .AsNoTracking()
            .Where(record => record.Status == OutboxStatus.Pending)
            .Select(record => record.CreatedAt)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false));

        DateTimeOffset now = _timeProvider.GetUtcNow();
        long oldestAge = pending.Length == 0 ? 0 : (long)Math.Max(0, (now - pending.Min()).TotalSeconds);
        AccountNumberSchemeDepletion[] schemes = (await _context.AccountNumberReservations.AsNoTracking()
            .Select(record => new { record.SchemeVersionId, record.SchemeId, record.Capacity, record.HighWaterMark, record.NextValue })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .Select(record => new AccountNumberSchemeDepletion(
                record.SchemeVersionId,
                record.SchemeId,
                record.Capacity,
                record.HighWaterMark,
                Math.Max(0, record.Capacity - record.NextValue),
                AccountNumberAllocationBand(record.HighWaterMark, record.Capacity)))
            .ToArray();
        return new ConfigurationReconciliationReport(
            now,
            [
                .. ConfigurationReconciliationPolicy.Evaluate(versions, publications),
                .. await EvaluateAccountsAsync(cancellationToken).ConfigureAwait(false),
            ],
            pending.LongLength,
            oldestAge,
            schemes);
    }

    private async Task<IReadOnlyList<ConfigurationReconciliationFinding>> EvaluateAccountsAsync(
        CancellationToken cancellationToken)
    {
        AccountReconciliationFacts[] accounts = (await _context.Accounts
            .AsNoTracking()
            .Select(record => new
            {
                record.AccountId,
                record.AccountApplicationId,
                record.Status,
                record.LedgerInitialisationStatus,
                record.LedgerAccountReference,
            })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .Select(record => new AccountReconciliationFacts(
                record.AccountId,
                record.AccountApplicationId,
                record.Status,
                record.LedgerInitialisationStatus,
                record.LedgerAccountReference))
            .ToArray();
        ApplicationReconciliationFacts[] applications = (await _context.AccountApplications
            .AsNoTracking()
            .Select(record => new { record.AccountApplicationId, record.Status })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .Select(record => new ApplicationReconciliationFacts(record.AccountApplicationId, record.Status))
            .ToArray();
        ClosureReconciliationFacts[] closures = (await _context.AccountClosureProposals
            .AsNoTracking()
            .Select(record => new { record.ProposalId, record.AccountId, record.Status, record.LedgerBalance, record.ConditionSnapshotHash })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .Select(record => new ClosureReconciliationFacts(
                record.ProposalId,
                record.AccountId,
                record.Status,
                record.LedgerBalance,
                record.ConditionSnapshotHash))
            .ToArray();
        return AccountReconciliationPolicy.Evaluate(accounts, applications, closures);
    }

    private static int AccountNumberAllocationBand(long highWaterMark, long capacity)
    {
        if (capacity <= 0)
        {
            return 0;
        }

        double used = (double)highWaterMark / capacity;
        return used switch
        {
            >= 0.95 => 95,
            >= 0.90 => 90,
            >= 0.80 => 80,
            _ => 0,
        };
    }
}
