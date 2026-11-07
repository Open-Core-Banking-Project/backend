using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// The in-process ledger answer to "what posted and when" for dormancy. The posting module belongs to the Ledger
/// epic (P1-E05), so the ledger's posted-movement store cannot yet contain a row: this adapter reports the store's
/// own version and an empty movement list, and fails closed with
/// <c>dormancy_activity_source_unavailable</c> when the account has no proven ledger account. It never substitutes
/// a summary, a cache or a channel log. Decision: P1-E04-D12.
/// </summary>
public sealed class LedgerQualifyingActivitySource : IAccountActivitySource
{
    private readonly CoreLedgerDbContext _context;
    private readonly TimeProvider _timeProvider;

    public LedgerQualifyingActivitySource(CoreLedgerDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<AccountActivitySnapshot> GetAsync(
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
            .ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (ledgerAccount is null)
        {
            return new AccountActivitySnapshot(
                Array.Empty<AccountMovementDescriptor>(),
                "ledger:uninitialised",
                now,
                AccountDormancyPolicy.ActivitySourceUnavailableCode);
        }

        IReadOnlyList<AccountMovementDescriptor> movements = Array.Empty<AccountMovementDescriptor>();
        return new AccountActivitySnapshot(
            movements,
            $"ledger:{ledgerAccount.LedgerAccountId}:movements:v{ledgerAccount.Version}",
            now,
            null);
    }
}

/// <summary>
/// The in-process ledger answer to the closure condition question. No posting exists yet, so the ledger reports a
/// zero ledger balance, zero available balance, zero holds, zero outstanding liens and no pending settlements with
/// its own source version. The account module records that answer and performs no balance arithmetic of its own.
/// Decision: P1-E04-D13.
/// </summary>
public sealed class LedgerClosureConditionSource : ILedgerClosureConditionSource
{
    private readonly CoreLedgerDbContext _context;
    private readonly TimeProvider _timeProvider;

    public LedgerClosureConditionSource(CoreLedgerDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<LedgerClosureConditions> GetAsync(
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
                "The account has no ledger account, so its closure conditions are unknown.");

        return new LedgerClosureConditions(
            ledgerAccount.LedgerAccountId,
            $"ledger:{ledgerAccount.LedgerAccountId}:closure:v{ledgerAccount.Version}",
            _timeProvider.GetUtcNow(),
            currencyCode,
            LedgerBalance: 0m,
            AvailableBalance: 0m,
            Holds: 0m,
            OutstandingLienAmount: 0m,
            PendingSettlements: 0);
    }
}
