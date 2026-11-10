using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Reads the current control version of one account across the three control tables. Decision: P1-E04-D10.
/// </summary>
internal static class AccountControlVersionReader
{
    /// <summary>
    /// The highest control version in force for the account, or zero when it carries no control. A SQL
    /// <c>MAX</c> over a nullable projection is used because <c>DefaultIfEmpty</c> over a scalar projection has
    /// no relational translation, so every control, dormancy, closure and summary call failed at runtime
    /// instead of returning a version.
    /// </summary>
    internal static async Task<long> ReadAsync(
        CoreLedgerDbContext context,
        string accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        string id = accountId.Trim();

        long restriction = await context.AccountRestrictions
            .AsNoTracking()
            .Where(record => record.AccountId == id)
            .MaxAsync(record => (long?)record.ControlVersion, cancellationToken)
            .ConfigureAwait(false) ?? 0L;
        long freeze = await context.AccountFreezes
            .AsNoTracking()
            .Where(record => record.AccountId == id)
            .MaxAsync(record => (long?)record.ControlVersion, cancellationToken)
            .ConfigureAwait(false) ?? 0L;
        long block = await context.AccountBlocks
            .AsNoTracking()
            .Where(record => record.AccountId == id)
            .MaxAsync(record => (long?)record.ControlVersion, cancellationToken)
            .ConfigureAwait(false) ?? 0L;

        return Math.Max(restriction, Math.Max(freeze, block));
    }
}
