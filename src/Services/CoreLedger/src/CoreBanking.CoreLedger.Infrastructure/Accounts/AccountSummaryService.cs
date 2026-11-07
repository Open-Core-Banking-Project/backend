using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Assembles one account summary from the account row, the ledger's availability answer and the module's own control
/// and lien records. It is a read model: when the ledger cannot answer, the balances are absent and the summary says
/// so rather than showing a zero. It is never a financial source. Decisions: P1-E04-D11, P1-E04-D15.
/// </summary>
public sealed class AccountSummaryService : IAccountSummaryService
{
    private readonly CoreLedgerDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILedgerAvailabilitySource _availability;

    public AccountSummaryService(
        CoreLedgerDbContext context,
        ITenantContext tenantContext,
        ILedgerAvailabilitySource availability)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
    }

    public async Task<AccountSummaryResult> GetAsync(
        InstitutionId institutionId,
        string accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (_tenantContext.CurrentInstitution != institutionId)
        {
            throw new AccountApplicationRejectedException(
                "cross_tenant_access_denied",
                "The authenticated institution does not match the account target.");
        }

        AccountRecord account = await _context.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.AccountId == accountId.Trim(), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AccountApplicationRejectedException(
                "account_not_found",
                "The account is not available in this institution.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        LedgerAvailabilitySnapshot? balances = null;
        string availability;
        try
        {
            balances = await _availability
                .GetAsync(institutionId, account.AccountId, account.CurrencyCode, cancellationToken)
                .ConfigureAwait(false);
            availability = AccountBalanceAvailability.Available;
        }
        catch (AccountApplicationRejectedException)
        {
            // A summary never invents a balance: an unproven ledger account yields no balances at all.
            availability = AccountBalanceAvailability.Unavailable;
        }

        decimal[] outstanding = await _context.AccountLiens
            .AsNoTracking()
            .Where(record => record.AccountId == account.AccountId
                && record.Status == "active"
                && record.EffectiveFrom <= now
                && (record.EffectiveTo == null || now < record.EffectiveTo))
            .Select(record => record.OutstandingAmount)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        int activeBlocks = await _context.AccountBlocks
            .AsNoTracking()
            .CountAsync(
                record => record.AccountId == account.AccountId
                    && record.Status == "active"
                    && record.EffectiveFrom <= now
                    && (record.EffectiveTo == null || now < record.EffectiveTo),
                cancellationToken)
            .ConfigureAwait(false);
        long controlVersion = await CurrentControlVersionAsync(account.AccountId, cancellationToken).ConfigureAwait(false);

        return new AccountSummaryResult(
            institutionId.Value,
            account.AccountId,
            account.MaskedAccountNumber,
            account.CustomerId,
            account.ProductId,
            account.ProductVersionId,
            account.BranchId,
            account.CurrencyCode,
            account.Status,
            account.Version,
            availability,
            account.LedgerAccountReference,
            balances,
            outstanding.Sum(),
            activeBlocks,
            controlVersion,
            balances?.AsOf ?? now);
    }

    private async Task<long> CurrentControlVersionAsync(string accountId, CancellationToken cancellationToken)
    {
        long restriction = await _context.AccountRestrictions
            .AsNoTracking()
            .Where(record => record.AccountId == accountId)
            .Select(record => record.ControlVersion)
            .DefaultIfEmpty(0)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);
        long freeze = await _context.AccountFreezes
            .AsNoTracking()
            .Where(record => record.AccountId == accountId)
            .Select(record => record.ControlVersion)
            .DefaultIfEmpty(0)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);
        long block = await _context.AccountBlocks
            .AsNoTracking()
            .Where(record => record.AccountId == accountId)
            .Select(record => record.ControlVersion)
            .DefaultIfEmpty(0)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);
        return Math.Max(restriction, Math.Max(freeze, block));
    }
}
