using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// The in-process ledger contract for Phase 1. It opens exactly one ledger account per bank account and
/// holds no amount, balance or posting column, so opening and activation can never create money.
/// Decision: P1-E04-D09.
/// </summary>
public sealed class LedgerAccountInitialisationService : ILedgerAccountInitialisation
{
    private const string InitialisedStatus = "initialised";

    private readonly CoreLedgerDbContext _context;
    private readonly TimeProvider _timeProvider;

    public LedgerAccountInitialisationService(CoreLedgerDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<LedgerInitialisationResult> InitialiseAsync(
        InstitutionId institutionId,
        string accountId,
        string currencyCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);

        LedgerAccountRecord? existing = await _context.LedgerAccounts
            .SingleOrDefaultAsync(record => record.AccountId == accountId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new LedgerInitialisationResult(accountId, existing.LedgerAccountId, existing.Status);
        }

        LedgerAccountRecord ledgerAccount = new()
        {
            InstitutionId = institutionId.Value,
            LedgerAccountId = Guid.NewGuid().ToString("N"),
            AccountId = accountId,
            CurrencyCode = currencyCode.Trim().ToUpperInvariant(),
            Status = InitialisedStatus,
            Version = 1,
            CreatedAt = _timeProvider.GetUtcNow(),
        };
        _context.LedgerAccounts.Add(ledgerAccount);
        return new LedgerInitialisationResult(accountId, ledgerAccount.LedgerAccountId, ledgerAccount.Status);
    }
}
