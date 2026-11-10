using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// The in-process ledger availability answer for Phase 1. No posting exists yet, so the ledger reports a zero
/// balance and zero holds with its own source version; the account module reports that answer and never
/// derives an available balance itself. Decision: P1-E04-D11.
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

        DateTimeOffset now = _timeProvider.GetUtcNow();
        return new LedgerAvailabilitySnapshot(
            LedgerBalance: 0m,
            Holds: 0m,
            Available: 0m,
            SourceVersion: $"ledger:{ledgerAccount.LedgerAccountId}:v{ledgerAccount.Version}",
            AsOf: now);
    }
}
