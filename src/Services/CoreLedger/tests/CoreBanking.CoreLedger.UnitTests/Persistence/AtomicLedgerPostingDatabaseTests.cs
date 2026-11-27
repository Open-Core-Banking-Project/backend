using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests.Persistence;

/// <summary>Real PostgreSQL atomicity and replay evidence for P1-E05 slice 6.</summary>
[Trait("Category", "Database")]
public sealed class AtomicLedgerPostingDatabaseTests
{
    private static readonly ActorId Actor = ActorId.From("ledger-db-actor");
    private static readonly string[] InternalTransferScope = ["internal_transfer"];

    [Fact]
    public async Task Posting_commits_balances_movements_audit_outbox_and_idempotency_once()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        string debitAccountId = await OpenLedgerAccountAsync(scope, "atomic-debit", time);
        string creditAccountId = await OpenLedgerAccountAsync(scope, "atomic-credit", time);
        await FundAsync(scope, debitAccountId, 100m);

        AmbientTenantContext tenant = scope.NewTenant();
        await using (CoreLedgerDbContext context = scope.CreateContext(tenant))
        {
            AtomicLedgerPostingService service = Service(context, tenant, time);
            LedgerPostingResult posted = await service.PostAsync(
                Command(scope, "atomic", debitAccountId, creditAccountId),
                CancellationToken.None);

            Assert.False(posted.Replayed);
        }

        await using (CoreLedgerDbContext verification = scope.CreateContext())
        {
            LedgerBalanceRecord debit = await verification.LedgerBalances.SingleAsync(x => x.LedgerAccountId == debitAccountId);
            LedgerBalanceRecord credit = await verification.LedgerBalances.SingleAsync(x => x.LedgerAccountId == creditAccountId);
            Assert.Equal(75m, debit.DebitTotal);
            Assert.Equal(100m, debit.CreditTotal);
            Assert.Equal(-25m, debit.NetBalance);
            Assert.Equal(75m, credit.CreditTotal);
            Assert.Equal(-75m, credit.NetBalance);
            Assert.Equal(2, await verification.LedgerMovements.CountAsync(x => x.JournalLineId.StartsWith("atomic-")));
            Assert.Equal(1, await verification.FinancialTransactions.CountAsync(x => x.FinancialTransactionId == "atomic-transaction"));
            Assert.Equal(1, await verification.InstitutionAuditEvents.CountAsync(x => x.ResourceId == "atomic-transaction"));
            Assert.Equal(1, await verification.OutboxMessages.CountAsync(x => x.BusinessReference == "atomic-transaction"));
            IdempotencyRecord outcome = await verification.IdempotencyRecords.SingleAsync(x => x.IdempotencyKey == "atomic-key");
            Assert.Equal(IdempotencyRecordStatus.Completed, outcome.Status);
        }

        AmbientTenantContext replayTenant = scope.NewTenant();
        await using (CoreLedgerDbContext replayContext = scope.CreateContext(replayTenant))
        {
            LedgerPostingResult replay = await Service(replayContext, replayTenant, time).PostAsync(
                Command(scope, "atomic", debitAccountId, creditAccountId, "atomic-key"),
                CancellationToken.None);

            Assert.True(replay.Replayed);
            Assert.Equal("atomic-transaction", replay.FinancialTransactionId);
        }

        await using CoreLedgerDbContext final = scope.CreateContext();
        Assert.Equal(2, await final.LedgerMovements.CountAsync(x => x.JournalLineId.StartsWith("atomic-")));
        Assert.Equal(1, await final.FinancialTransactions.CountAsync(x => x.FinancialTransactionId == "atomic-transaction"));
    }

    [Fact]
    public async Task Missing_balance_rolls_back_the_idempotency_claim_and_all_posting_evidence()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        string existingAccountId = await OpenLedgerAccountAsync(scope, "rollback-existing", time);
        AmbientTenantContext tenant = scope.NewTenant();
        await using CoreLedgerDbContext context = scope.CreateContext(tenant);

        LedgerPostingException exception = await Assert.ThrowsAsync<LedgerPostingException>(() =>
            Service(context, tenant, time).PostAsync(
                Command(scope, "rollback", existingAccountId, "missing-ledger-account"),
                CancellationToken.None));

        Assert.Equal("ledger_account_not_found", exception.Code);
        context.ChangeTracker.Clear();
        Assert.False(await context.IdempotencyRecords.AnyAsync(x => x.IdempotencyKey == "rollback-key"));
        Assert.False(await context.FinancialTransactions.AnyAsync(x => x.FinancialTransactionId == "rollback-transaction"));
    }

    [Fact]
    public async Task Concurrent_debits_cannot_overspend_one_locked_balance()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        string source = await OpenLedgerAccountAsync(scope, "concurrent-source", time);
        string firstRecipient = await OpenLedgerAccountAsync(scope, "concurrent-first", time);
        string secondRecipient = await OpenLedgerAccountAsync(scope, "concurrent-second", time);
        await FundAsync(scope, source, 100m);

        Task<Exception?> first = CapturePostingAsync(scope, time, Command(scope, "concurrent-a", source, firstRecipient));
        Task<Exception?> second = CapturePostingAsync(scope, time, Command(scope, "concurrent-b", source, secondRecipient));
        Exception?[] outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, outcome => outcome is null);
        LedgerPostingException rejection = Assert.Single(outcomes.OfType<LedgerPostingException>());
        Assert.Equal("insufficient_available_funds", rejection.Code);

        await using CoreLedgerDbContext verification = scope.CreateContext();
        LedgerBalanceRecord balance = await verification.LedgerBalances.SingleAsync(x => x.LedgerAccountId == source);
        Assert.Equal(25m, -balance.NetBalance);
        Assert.Equal(1, await verification.FinancialTransactions.CountAsync(x => x.FinancialTransactionId.StartsWith("concurrent-")));
    }

    [Fact]
    public async Task Applicable_block_refuses_posting_before_any_financial_evidence()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        string source = await OpenLedgerAccountAsync(scope, "blocked-source", time);
        string recipient = await OpenLedgerAccountAsync(scope, "blocked-recipient", time);
        await FundAsync(scope, source, 100m);
        await AddBlockAsync(scope, source);

        AmbientTenantContext tenant = scope.NewTenant();
        await using CoreLedgerDbContext context = scope.CreateContext(tenant);
        LedgerPostingException rejection = await Assert.ThrowsAsync<LedgerPostingException>(() =>
            Service(context, tenant, time).PostAsync(
                Command(scope, "blocked", source, recipient, debitControlVersion: 1),
                CancellationToken.None));

        Assert.Equal("account_control_denied:legal_block", rejection.Code);
        context.ChangeTracker.Clear();
        Assert.False(await context.FinancialTransactions.AnyAsync(x => x.FinancialTransactionId == "blocked-transaction"));
    }

    [Fact]
    public async Task Stale_control_version_fails_closed()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        string source = await OpenLedgerAccountAsync(scope, "stale-source", time);
        string recipient = await OpenLedgerAccountAsync(scope, "stale-recipient", time);
        await FundAsync(scope, source, 100m);
        await AddBlockAsync(scope, source);

        AmbientTenantContext tenant = scope.NewTenant();
        await using CoreLedgerDbContext context = scope.CreateContext(tenant);
        LedgerPostingException rejection = await Assert.ThrowsAsync<LedgerPostingException>(() =>
            Service(context, tenant, time).PostAsync(
                Command(scope, "stale", source, recipient),
                CancellationToken.None));

        Assert.Equal("control_version_stale", rejection.Code);
    }

    [Fact]
    public async Task Outstanding_lien_reduces_available_funds_without_becoming_a_control_denial()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        string source = await OpenLedgerAccountAsync(scope, "lien-source", time);
        string recipient = await OpenLedgerAccountAsync(scope, "lien-recipient", time);
        await FundAsync(scope, source, 100m);
        await AddLienAsync(scope, source, 40m);

        AmbientTenantContext tenant = scope.NewTenant();
        await using CoreLedgerDbContext context = scope.CreateContext(tenant);
        LedgerPostingException rejection = await Assert.ThrowsAsync<LedgerPostingException>(() =>
            Service(context, tenant, time).PostAsync(
                Command(scope, "lien", source, recipient),
                CancellationToken.None));

        Assert.Equal("insufficient_available_funds", rejection.Code);
    }

    private static async Task<string> OpenLedgerAccountAsync(
        CoreLedgerDatabaseScope scope,
        string suffix,
        TimeProvider time)
    {
        string accountId = await AccountDatabaseScenario.OpenAccountAsync(
            scope,
            suffix + "-application",
            suffix + "-customer",
            Actor,
            time);
        await using CoreLedgerDbContext context = scope.CreateContext();
        return await context.LedgerAccounts
            .Where(x => x.AccountId == accountId)
            .Select(x => x.LedgerAccountId)
            .SingleAsync();
    }

    private static AtomicLedgerPostingService Service(
        CoreLedgerDbContext context,
        AmbientTenantContext tenant,
        TimeProvider time) =>
        new(
            context,
            new FinancialIdempotencyService(context, tenant, time),
            new AccountControlService(context, new IdempotencyService(context, time), tenant, time),
            tenant,
            time);

    private static async Task<Exception?> CapturePostingAsync(
        CoreLedgerDatabaseScope scope,
        TimeProvider time,
        AtomicLedgerPostingCommand command)
    {
        AmbientTenantContext tenant = scope.NewTenant();
        await using CoreLedgerDbContext context = scope.CreateContext(tenant);
        try
        {
            await Service(context, tenant, time).PostAsync(command, CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task FundAsync(CoreLedgerDatabaseScope scope, string ledgerAccountId, decimal amount)
    {
        await using CoreLedgerDbContext context = scope.CreateContext();
        LedgerBalanceRecord balance = await context.LedgerBalances.SingleAsync(
            record => record.LedgerAccountId == ledgerAccountId);
        balance.CreditTotal = amount;
        balance.NetBalance = -amount;
        balance.Version++;
        balance.AsOf = CoreLedgerDatabaseScope.SeededAt;
        await context.SaveChangesAsync();
    }

    private static async Task AddBlockAsync(CoreLedgerDatabaseScope scope, string ledgerAccountId)
    {
        await using CoreLedgerDbContext context = scope.CreateContext();
        string accountId = await context.LedgerAccounts
            .Where(record => record.LedgerAccountId == ledgerAccountId)
            .Select(record => record.AccountId)
            .SingleAsync();
        context.AccountBlocks.Add(new AccountBlockRecord
        {
            InstitutionId = scope.Institution.Value,
            BlockId = Guid.NewGuid().ToString("N"),
            AccountId = accountId,
            BlockTypeCode = "legal",
            OperationScopeJson = JsonSerializer.Serialize(InternalTransferScope),
            ReasonCode = "test_block",
            AuthorityReference = "test-authority",
            Status = "active",
            RequiresManualRelease = false,
            RequestId = Guid.NewGuid().ToString("N"),
            AppliedByActorId = Actor.Value,
            ControlVersion = 1,
            EffectiveFrom = CoreLedgerDatabaseScope.SeededAt.AddMinutes(-1),
        });
        await context.SaveChangesAsync();
    }

    private static async Task AddLienAsync(CoreLedgerDatabaseScope scope, string ledgerAccountId, decimal amount)
    {
        await using CoreLedgerDbContext context = scope.CreateContext();
        string accountId = await context.LedgerAccounts
            .Where(record => record.LedgerAccountId == ledgerAccountId)
            .Select(record => record.AccountId)
            .SingleAsync();
        context.AccountLiens.Add(new AccountLienRecord
        {
            InstitutionId = scope.Institution.Value,
            LienId = Guid.NewGuid().ToString("N"),
            AccountId = accountId,
            PriorityCode = "institution",
            PriorityRank = 1,
            Amount = amount,
            OutstandingAmount = amount,
            CurrencyCode = "NGN",
            ReasonCode = "test_lien",
            Status = "active",
            RequestId = Guid.NewGuid().ToString("N"),
            CreatedByActorId = Actor.Value,
            EffectiveFrom = CoreLedgerDatabaseScope.SeededAt.AddMinutes(-1),
            CreatedAt = CoreLedgerDatabaseScope.SeededAt.AddMinutes(-1),
        });
        await context.SaveChangesAsync();
    }

    private static AtomicLedgerPostingCommand Command(
        CoreLedgerDatabaseScope scope,
        string suffix,
        string debitAccountId,
        string creditAccountId,
        string? idempotencyKey = null,
        long debitControlVersion = 0)
    {
        FinancialDates dates = FinancialDates.Create(
            CoreLedgerDatabaseScope.SeededAt,
            new DateOnly(2026, 11, 8),
            new DateOnly(2026, 11, 8),
            new DateOnly(2026, 11, 8));
        string hash = CanonicalFinancialRequest.Create("""{"amount":75,"currency":"NGN","eventCode":"internal_transfer.v1"}""").Hash;
        FinancialTransaction transaction = FinancialTransaction.CreateReadyToPost(
            suffix + "-transaction",
            suffix + "-TX",
            scope.Institution.Value,
            "payments-vtu",
            suffix + "-source",
            "internal_transfer.v1",
            hash,
            dates);
        AccountingScope accountingScope = new(scope.Institution.Value, "legal-entity-1", CurrencyCode.Parse("NGN"));
        JournalEntry journal = JournalEntry.Create(
            suffix + "-journal",
            transaction.Id,
            suffix + "-JRN",
            "mapping-version-1",
            accountingScope,
            dates,
            [
                JournalLine.Create(suffix + "-debit-line", 1, debitAccountId, DebitCreditIndicator.Debit,
                    AccountingComponentType.Principal, Money.Create(75m, "NGN"), accountingScope),
                JournalLine.Create(suffix + "-credit-line", 2, creditAccountId, DebitCreditIndicator.Credit,
                    AccountingComponentType.Principal, Money.Create(75m, "NGN"), accountingScope),
            ]);
        PostingIdempotencyScope idempotency = PostingIdempotencyScope.Create(
            scope.Institution.Value,
            "ledger.transaction.post",
            "payments-vtu",
            idempotencyKey ?? suffix + "-key");
        return new AtomicLedgerPostingCommand(
            idempotency,
            transaction,
            journal,
            new LedgerPostingControlContext(
                "api",
                "internal_transfer",
                new Dictionary<string, long>(StringComparer.Ordinal)
                {
                    [debitAccountId] = debitControlVersion,
                    [creditAccountId] = 0,
                }),
            Actor.Value,
            "service",
            suffix + "-correlation");
    }
}
