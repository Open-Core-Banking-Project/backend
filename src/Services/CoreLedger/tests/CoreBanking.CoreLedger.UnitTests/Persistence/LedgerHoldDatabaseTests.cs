using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Ledger;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests.Persistence;

/// <summary>Real PostgreSQL lifecycle and capture contention evidence for P1-E05 slice 8.</summary>
[Trait("Category", "Database")]
public sealed class LedgerHoldDatabaseTests
{
    private static readonly ActorId Actor = ActorId.From("hold-db-actor");

    [Fact]
    public async Task Create_and_release_change_only_the_reserved_component_and_replay_once()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        string ledgerAccountId = await OpenLedgerAccountAsync(scope, "hold-release", time);
        await FundAsync(scope, ledgerAccountId, 100m);

        LedgerHoldResult created;
        AmbientTenantContext createTenant = scope.NewTenant();
        await using (CoreLedgerDbContext createContext = scope.CreateContext(createTenant))
        {
            created = await HoldService(createContext, createTenant, time).CreateAsync(
                CreateCommand(scope, "release", ledgerAccountId, 60m),
                CancellationToken.None);
        }

        AmbientTenantContext releaseTenant = scope.NewTenant();
        await using (CoreLedgerDbContext releaseContext = scope.CreateContext(releaseTenant))
        {
            LedgerHoldResult released = await HoldService(releaseContext, releaseTenant, time).ReleaseAsync(
                ReleaseCommand(scope, created, 20m),
                CancellationToken.None);
            Assert.Equal(40m, released.RemainingAmount);
            Assert.Equal(20m, released.ReleasedAmount);
            Assert.Equal(2, released.Version);
        }

        AmbientTenantContext replayTenant = scope.NewTenant();
        await using (CoreLedgerDbContext replayContext = scope.CreateContext(replayTenant))
        {
            LedgerHoldResult replay = await HoldService(replayContext, replayTenant, time).ReleaseAsync(
                ReleaseCommand(scope, created, 20m),
                CancellationToken.None);
            Assert.True(replay.Replayed);
            Assert.Equal(40m, replay.RemainingAmount);
        }

        await using CoreLedgerDbContext verification = scope.CreateContext();
        LedgerBalanceRecord balance = await verification.LedgerBalances.SingleAsync(x => x.LedgerAccountId == ledgerAccountId);
        Assert.Equal(40m, balance.HoldRemaining);
        Assert.Equal(-100m, balance.NetBalance);
        Assert.Equal(2, await verification.LedgerHoldHistory.CountAsync(x => x.HoldId == created.HoldId));
    }

    [Fact]
    public async Task Capture_and_financial_posting_commit_in_one_transaction_without_double_deducting_the_hold()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        string source = await OpenLedgerAccountAsync(scope, "hold-capture-source", time);
        string recipient = await OpenLedgerAccountAsync(scope, "hold-capture-recipient", time);
        await FundAsync(scope, source, 100m);
        LedgerHoldResult hold = await CreateHoldAsync(scope, time, "capture", source, 75m);

        AmbientTenantContext tenant = scope.NewTenant();
        await using CoreLedgerDbContext context = scope.CreateContext(tenant);
        LedgerPostingResult posting = await PostingService(context, tenant, time).PostAsync(
            PostingCommand(scope, "capture", source, recipient, new LedgerHoldCapture(hold.HoldId, 75m, hold.Version)),
            CancellationToken.None);

        Assert.False(posting.Replayed);
        context.ChangeTracker.Clear();
        LedgerHoldRecord captured = await context.LedgerHolds.SingleAsync(x => x.HoldId == hold.HoldId);
        LedgerBalanceRecord sourceBalance = await context.LedgerBalances.SingleAsync(x => x.LedgerAccountId == source);
        LedgerHoldHistoryRecord history = await context.LedgerHoldHistory.SingleAsync(
            x => x.HoldId == hold.HoldId && x.Transition == "captured");
        Assert.Equal("captured", captured.Status);
        Assert.Equal(0m, captured.RemainingAmount);
        Assert.Equal(75m, captured.CapturedAmount);
        Assert.Equal(0m, sourceBalance.HoldRemaining);
        Assert.Equal(-25m, sourceBalance.NetBalance);
        Assert.Equal(posting.FinancialTransactionId, history.FinancialTransactionId);
    }

    [Fact]
    public async Task Concurrent_capture_requests_cannot_consume_the_same_hold_twice()
    {
        CoreLedgerDatabaseScope scope = new();
        FixedTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        string source = await OpenLedgerAccountAsync(scope, "hold-race-source", time);
        string firstRecipient = await OpenLedgerAccountAsync(scope, "hold-race-first", time);
        string secondRecipient = await OpenLedgerAccountAsync(scope, "hold-race-second", time);
        await FundAsync(scope, source, 50m);
        LedgerHoldResult hold = await CreateHoldAsync(scope, time, "race", source, 50m);

        Task<Exception?> first = CapturePostingAsync(
            scope, time, PostingCommand(scope, "race-a", source, firstRecipient, new LedgerHoldCapture(hold.HoldId, 50m, 1)));
        Task<Exception?> second = CapturePostingAsync(
            scope, time, PostingCommand(scope, "race-b", source, secondRecipient, new LedgerHoldCapture(hold.HoldId, 50m, 1)));
        Exception?[] outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, outcome => outcome is null);
        LedgerPostingException rejection = Assert.Single(outcomes.OfType<LedgerPostingException>());
        Assert.Equal("ledger_hold_version_stale", rejection.Code);

        await using CoreLedgerDbContext verification = scope.CreateContext();
        LedgerHoldRecord captured = await verification.LedgerHolds.SingleAsync(x => x.HoldId == hold.HoldId);
        Assert.Equal(50m, captured.CapturedAmount);
        Assert.Equal(1, await verification.LedgerHoldHistory.CountAsync(
            x => x.HoldId == hold.HoldId && x.Transition == "captured"));
        Assert.Equal(1, await verification.FinancialTransactions.CountAsync(
            x => x.FinancialTransactionId.StartsWith("race-")));
    }

    private static async Task<LedgerHoldResult> CreateHoldAsync(
        CoreLedgerDatabaseScope scope,
        TimeProvider time,
        string suffix,
        string ledgerAccountId,
        decimal amount)
    {
        AmbientTenantContext tenant = scope.NewTenant();
        await using CoreLedgerDbContext context = scope.CreateContext(tenant);
        return await HoldService(context, tenant, time).CreateAsync(
            CreateCommand(scope, suffix, ledgerAccountId, amount),
            CancellationToken.None);
    }

    private static async Task<Exception?> CapturePostingAsync(
        CoreLedgerDatabaseScope scope,
        TimeProvider time,
        AtomicLedgerPostingCommand command)
    {
        AmbientTenantContext tenant = scope.NewTenant();
        await using CoreLedgerDbContext context = scope.CreateContext(tenant);
        try
        {
            await PostingService(context, tenant, time).PostAsync(command, CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static LedgerHoldService HoldService(
        CoreLedgerDbContext context,
        AmbientTenantContext tenant,
        TimeProvider time) =>
        new(
            context,
            new FinancialIdempotencyService(context, tenant, time),
            new AccountControlService(context, new IdempotencyService(context, time), tenant, time),
            tenant,
            time);

    private static AtomicLedgerPostingService PostingService(
        CoreLedgerDbContext context,
        AmbientTenantContext tenant,
        TimeProvider time) =>
        new(
            context,
            new FinancialIdempotencyService(context, tenant, time),
            new AccountControlService(context, new IdempotencyService(context, time), tenant, time),
            tenant,
            time);

    private static CreateLedgerHoldCommand CreateCommand(
        CoreLedgerDatabaseScope scope,
        string suffix,
        string ledgerAccountId,
        decimal amount)
    {
        string hash = CanonicalFinancialRequest.Create(
            $$"""{"amount":{{amount}},"currency":"NGN","ledgerAccountId":"{{ledgerAccountId}}"}""").Hash;
        return new CreateLedgerHoldCommand(
            PostingIdempotencyScope.Create(scope.Institution.Value, "ledger.hold.create", "payments-vtu", suffix + "-hold-key"),
            hash,
            ledgerAccountId,
            "payments-vtu",
            suffix + "-source",
            "payment_authorisation",
            Money.Create(amount, "NGN"),
            CoreLedgerDatabaseScope.SeededAt.AddHours(1),
            "api",
            "internal_transfer",
            0,
            Actor.Value,
            "service",
            suffix + "-hold-correlation");
    }

    private static ReleaseLedgerHoldCommand ReleaseCommand(
        CoreLedgerDatabaseScope scope,
        LedgerHoldResult hold,
        decimal amount)
    {
        string hash = CanonicalFinancialRequest.Create(
            $$"""{"amount":{{amount}},"holdId":"{{hold.HoldId}}"}""").Hash;
        return new ReleaseLedgerHoldCommand(
            PostingIdempotencyScope.Create(scope.Institution.Value, "ledger.hold.release", "payments-vtu", "release-key"),
            hash,
            hold.HoldId,
            amount,
            "payment_cancelled",
            hold.Version,
            Actor.Value,
            "service",
            "release-correlation");
    }

    private static AtomicLedgerPostingCommand PostingCommand(
        CoreLedgerDatabaseScope scope,
        string suffix,
        string source,
        string recipient,
        LedgerHoldCapture capture)
    {
        FinancialDates dates = FinancialDates.Create(
            CoreLedgerDatabaseScope.SeededAt,
            new DateOnly(2026, 11, 8),
            new DateOnly(2026, 11, 8),
            new DateOnly(2026, 11, 8));
        string hash = CanonicalFinancialRequest.Create(
            $$"""{"amount":{{capture.Amount}},"currency":"NGN","holdId":"{{capture.HoldId}}"}""").Hash;
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
                JournalLine.Create(suffix + "-debit-line", 1, source, DebitCreditIndicator.Debit,
                    AccountingComponentType.Principal, Money.Create(capture.Amount, "NGN"), accountingScope),
                JournalLine.Create(suffix + "-credit-line", 2, recipient, DebitCreditIndicator.Credit,
                    AccountingComponentType.Principal, Money.Create(capture.Amount, "NGN"), accountingScope),
            ]);
        return new AtomicLedgerPostingCommand(
            PostingIdempotencyScope.Create(scope.Institution.Value, "ledger.transaction.post", "payments-vtu", suffix + "-key"),
            transaction,
            journal,
            new LedgerPostingControlContext(
                "api",
                "internal_transfer",
                new Dictionary<string, long>(StringComparer.Ordinal)
                {
                    [source] = 0,
                    [recipient] = 0,
                }),
            Actor.Value,
            "service",
            suffix + "-correlation",
            HoldCapture: capture);
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
}
