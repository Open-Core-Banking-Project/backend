using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Reads the authoritative ledger balance and applies the approved availability formula. The account
/// module reports this answer and never derives a balance itself. Decisions: P1-E04-D11, P1-E05-D06.
/// </summary>
public sealed class LedgerAvailabilitySource : ILedgerAvailabilitySource
{
    private readonly CoreLedgerDbContext _context;
    private readonly TimeProvider _timeProvider;

    public LedgerAvailabilitySource(CoreLedgerDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<LedgerAvailabilitySnapshot> GetAsync(
        InstitutionId institutionId,
        string accountId,
        string currencyCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        LedgerAccountRecord? ledgerAccount = await _context.LedgerAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.AccountId == accountId.Trim(), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AccountApplicationRejectedException(
                "ledger_initialisation_unproven",
                "The account has no ledger account, so its availability is unknown.");

        LedgerBalanceRecord balance = await _context.LedgerBalances
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.LedgerAccountId == ledgerAccount.LedgerAccountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AccountApplicationRejectedException(
                "ledger_balance_unproven",
                "The account has no authoritative ledger balance, so its availability is unknown.");

        DateTimeOffset now = _timeProvider.GetUtcNow();
        decimal outstandingLiens = await _context.AccountLiens
            .AsNoTracking()
            .Where(record => record.AccountId == accountId
                && record.Status == "active"
                && record.EffectiveFrom <= now
                && (record.EffectiveTo == null || now < record.EffectiveTo))
            .SumAsync(record => (decimal?)record.OutstandingAmount, cancellationToken)
            .ConfigureAwait(false) ?? 0m;
        LedgerBalance ledgerBalance = LedgerBalance.Restore(
            balance.LedgerAccountId,
            balance.CurrencyCode,
            ParseNormalSide(balance.NormalSide),
            balance.DebitTotal,
            balance.CreditTotal,
            balance.CreditLimit,
            balance.HoldRemaining,
            balance.PendingAuthorised,
            balance.Version);
        AvailableFundsPosition position = ledgerBalance.Available(outstandingLiens);

        return new LedgerAvailabilitySnapshot(
            LedgerBalance: position.VisibleLedgerBalance,
            Holds: balance.HoldRemaining,
            Available: position.Available,
            SourceVersion: $"ledger:{ledgerAccount.LedgerAccountId}:v{balance.Version}",
            AsOf: balance.AsOf <= now ? balance.AsOf : now);
    }

    private static DebitCreditIndicator ParseNormalSide(string value) => value switch
    {
        "debit" => DebitCreditIndicator.Debit,
        "credit" => DebitCreditIndicator.Credit,
        _ => throw new AccountApplicationRejectedException(
            "ledger_balance_normal_side_invalid",
            "The authoritative ledger balance has an invalid normal side."),
    };
}
