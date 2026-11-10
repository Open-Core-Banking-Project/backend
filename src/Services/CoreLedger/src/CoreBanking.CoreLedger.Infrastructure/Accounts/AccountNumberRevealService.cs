using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Returns exactly one account's full number to an already-authorised caller and writes audit evidence
/// that names the actor, permission, reason, correlation and account, never the number itself.
/// Decision: P1-E04-D06.
/// </summary>
public sealed class AccountNumberRevealService : IAccountNumberRevealService
{
    private readonly CoreLedgerDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public AccountNumberRevealService(
        CoreLedgerDbContext context,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<AccountNumberRevealResult> RevealAsync(
        RevealAccountNumberCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.InstitutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.AccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        if (_tenantContext.CurrentInstitution != command.InstitutionId)
        {
            throw new AccountApplicationRejectedException(
                "cross_tenant_access_denied",
                "The authenticated institution does not match the account target.");
        }

        string reason = command.Reason.Trim();
        if (reason.Length is < 3 or > 256)
        {
            throw new AccountApplicationRejectedException(
                "account_number_reveal_reason_invalid",
                "A reveal requires a reason of between 3 and 256 characters.");
        }

        string accountId = command.AccountId.Trim();
        AccountRecord account = await _context.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.AccountId == accountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AccountApplicationRejectedException(
                "account_not_found",
                "The account is not available in this institution.");

        DateTimeOffset now = _timeProvider.GetUtcNow();
        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = command.InstitutionId.Value,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = command.ActorId.Value,
            ActorType = "human",
            Permission = AccountPermissions.NumberReveal.Value,
            Action = "account.number.reveal",
            ResourceType = "account.account",
            ResourceId = account.AccountId,
            Outcome = "succeeded",
            Reason = reason,
            CorrelationId = command.CorrelationId,
            BeforeReference = account.MaskedAccountNumber,
            OccurredAt = now,
        });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AccountNumberRevealResult(account.AccountId, account.AccountNumber, now);
    }
}
