using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Counts the holdings that consume a multiple-account limit for one customer: every application
/// that is in flight or open, in the counted statuses only. Until P1-E04-S8 creates account records,
/// the application is the durable record of each opening, so this reader is the counted source.
/// Decision: P1-E04-D07.
/// </summary>
public sealed class AccountHoldingsReader : IAccountHoldingsReader
{
    private readonly CoreLedgerDbContext _context;

    public AccountHoldingsReader(CoreLedgerDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<IReadOnlyCollection<AccountHolding>> GetAsync(
        InstitutionId institutionId,
        string customerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        string[] countedStatuses = Enum.GetValues<AccountApplicationStatus>()
            .Where(AccountApplicationLifecycle.CountsTowardsLimit)
            .Select(status => status.ToString())
            .ToArray();

        AccountHolding[] holdings = await _context.AccountApplications
            .AsNoTracking()
            .Where(record => record.InstitutionId == institutionId.Value
                && record.CustomerId == customerId
                && countedStatuses.Contains(record.Status))
            .Select(record => new AccountHolding(
                record.AccountApplicationId,
                record.Status,
                record.ProductCode,
                record.ProductFamily,
                record.CurrencyCode))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return holdings;
    }
}
