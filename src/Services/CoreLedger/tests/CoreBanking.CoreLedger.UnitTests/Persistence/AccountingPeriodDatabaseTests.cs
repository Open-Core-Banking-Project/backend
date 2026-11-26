using System;
using System.Collections.Generic;
using System.Globalization;
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

/// <summary>Real PostgreSQL evidence for accounting periods and the approved date policy. Decisions: P1-E05-D12, D13.</summary>
[Trait("Category", "Database")]
public sealed class AccountingPeriodDatabaseTests
{
    private static readonly ActorId Actor = ActorId.From("accounting-period-actor");
    private static readonly DateOnly BusinessDate = new(2026, 11, 8);

    [Fact]
    public async Task Posting_provisions_an_open_period_and_records_its_history()
    {
        CoreLedgerDatabaseScope scope = new();
        MutableTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        await PostAsync(scope, time, "provision");

        await using CoreLedgerDbContext verification = scope.CreateContext();
        AccountingPeriodRecord period = await verification.AccountingPeriods.SingleAsync(
            record => record.InstitutionId == scope.Institution.Value);
        Assert.Equal("2026-11", period.PeriodCode);
        Assert.Equal("open", period.Status);
        Assert.Equal(1, period.Version);
        Assert.Equal(1, await verification.AccountingPeriodHistory.CountAsync(
            record => record.PeriodCode == "2026-11"));
    }

    [Fact]
    public async Task A_soft_closed_period_only_posts_its_published_allow_list()
    {
        CoreLedgerDatabaseScope allowed = new();
        MutableTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        await using (CoreLedgerDbContext context = allowed.CreateContext(allowed.NewTenant()))
        {
            AccountingPeriodService periods = Service(context, allowed, time);
            await periods.OpenAsync(
                new OpenAccountingPeriodCommand(allowed.Institution, "legal-entity-1", 2026, 11, Actor, "open"),
                CancellationToken.None);
            await periods.SoftCloseAsync(
                new SoftCloseAccountingPeriodCommand(
                    allowed.Institution,
                    "legal-entity-1",
                    2026,
                    11,
                    Actor,
                    1,
                    ["internal_transfer.v1"],
                    "soft-close-approval",
                    "month_end",
                    "soft-close"),
                CancellationToken.None);
        }

        await PostAsync(allowed, time, "soft-allowed");

        CoreLedgerDatabaseScope denied = new();
        await using (CoreLedgerDbContext context = denied.CreateContext(denied.NewTenant()))
        {
            AccountingPeriodService periods = Service(context, denied, time);
            await periods.OpenAsync(
                new OpenAccountingPeriodCommand(denied.Institution, "legal-entity-1", 2026, 11, Actor, "open"),
                CancellationToken.None);
            await periods.SoftCloseAsync(
                new SoftCloseAccountingPeriodCommand(
                    denied.Institution, "legal-entity-1", 2026, 11, Actor, 1, [], "soft-close-approval", "month_end", "soft-close"),
                CancellationToken.None);
        }

        LedgerPostingException refusal = await Assert.ThrowsAsync<LedgerPostingException>(
            () => PostAsync(denied, time, "soft-denied"));
        Assert.Equal("accounting_period_soft_closed_denied", refusal.Code);
    }

    [Fact]
    public async Task Closed_and_locked_periods_refuse_posting()
    {
        CoreLedgerDatabaseScope scope = new();
        MutableTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        await using CoreLedgerDbContext context = scope.CreateContext(scope.NewTenant());
        AccountingPeriodService periods = Service(context, scope, time);
        await periods.OpenAsync(
            new OpenAccountingPeriodCommand(scope.Institution, "legal-entity-1", 2026, 11, Actor, "open"),
            CancellationToken.None);
        AccountingPeriodResult closed = await periods.CloseAsync(
            new CloseAccountingPeriodCommand(
                scope.Institution,
                "legal-entity-1",
                2026,
                11,
                Actor,
                1,
                100m,
                100m,
                0,
                true,
                true,
                "close-approval",
                new string('a', 64),
                "close"),
            CancellationToken.None);
        Assert.Equal("closed", closed.Status);

        LedgerPostingException closedRefusal = await Assert.ThrowsAsync<LedgerPostingException>(
            () => PostAsync(scope, time, "closed"));
        Assert.Equal("accounting_period_closed", closedRefusal.Code);

        AccountingPeriodResult locked = await periods.LockAsync(
            new LockAccountingPeriodCommand(
                scope.Institution, "legal-entity-1", 2026, 11, Actor, closed.Version, "lock-approval", "lock"),
            CancellationToken.None);
        Assert.Equal("locked", locked.Status);

        LedgerPostingException lockedRefusal = await Assert.ThrowsAsync<LedgerPostingException>(
            () => PostAsync(scope, time, "locked"));
        Assert.Equal("accounting_period_closed", lockedRefusal.Code);
    }

    [Fact]
    public async Task Concurrent_transitions_are_serialized_to_one_winner()
    {
        CoreLedgerDatabaseScope scope = new();
        MutableTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        await using (CoreLedgerDbContext context = scope.CreateContext(scope.NewTenant()))
        {
            await Service(context, scope, time).OpenAsync(
                new OpenAccountingPeriodCommand(scope.Institution, "legal-entity-1", 2026, 11, Actor, "open"),
                CancellationToken.None);
        }

        Task<object> first = CloseAsync(scope, time, "concurrent-a");
        Task<object> second = CloseAsync(scope, time, "concurrent-b");
        object[] outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, outcome => outcome is AccountingPeriodResult);
        LedgerPostingException refusal = Assert.Single(outcomes.OfType<LedgerPostingException>());
        Assert.Equal("accounting_period_version_stale", refusal.Code);
        await using CoreLedgerDbContext verification = scope.CreateContext();
        Assert.Equal("closed", (await verification.AccountingPeriods.SingleAsync(
            record => record.InstitutionId == scope.Institution.Value)).Status);
    }

    [Fact]
    public async Task Reopen_needs_two_distinct_approvers_and_expires()
    {
        CoreLedgerDatabaseScope scope = new();
        MutableTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);
        await using CoreLedgerDbContext context = scope.CreateContext(scope.NewTenant());
        AccountingPeriodService periods = Service(context, scope, time);
        await periods.OpenAsync(
            new OpenAccountingPeriodCommand(scope.Institution, "legal-entity-1", 2026, 11, Actor, "open"),
            CancellationToken.None);
        AccountingPeriodResult closed = await CloseAsync(context, scope, periods);

        ReopenAccountingPeriodCommand single = ReopenCommand(scope, closed.Version, [new("finance-one", "approval-one", time.GetUtcNow())]);
        Assert.Equal(
            "accounting_period_approval_insufficient",
            (await Assert.ThrowsAsync<LedgerPostingException>(() => periods.ReopenAsync(single, CancellationToken.None))).Code);

        ReopenAccountingPeriodCommand self = ReopenCommand(
            scope,
            closed.Version,
            [new(Actor.Value, "approval-self", time.GetUtcNow()), new("finance-two", "approval-two", time.GetUtcNow())]);
        Assert.Equal(
            "accounting_period_self_approval_denied",
            (await Assert.ThrowsAsync<LedgerPostingException>(() => periods.ReopenAsync(self, CancellationToken.None))).Code);

        ReopenAccountingPeriodCommand valid = ReopenCommand(
            scope,
            closed.Version,
            [new("finance-one", "approval-one", time.GetUtcNow()), new("finance-two", "approval-two", time.GetUtcNow())]);
        AccountingPeriodResult reopened = await periods.ReopenAsync(valid, CancellationToken.None);
        Assert.Equal("reopened", reopened.Status);
        Assert.NotNull(reopened.ReopenExpiresAt);

        await PostAsync(scope, time, "reopened-allowed");

        time.Now = time.Now.AddHours(2);
        LedgerPostingException expired = await Assert.ThrowsAsync<LedgerPostingException>(
            () => PostAsync(scope, time, "reopened-expired"));
        Assert.Equal("accounting_period_reopen_expired", expired.Code);
    }

    [Fact]
    public async Task A_historical_value_date_needs_authority_and_records_a_recalculation_handoff()
    {
        CoreLedgerDatabaseScope scope = new();
        MutableTimeProvider time = new(CoreLedgerDatabaseScope.SeededAt);

        LedgerPostingException unapproved = await Assert.ThrowsAsync<LedgerPostingException>(
            () => PostAsync(scope, time, "historical-denied", BusinessDate.AddDays(-120)));
        Assert.Equal("backdate_authority_required", unapproved.Code);

        DateAuthority authority = new("backdate-approval", "customer_refund", "case-120", 180, StepUpVerified: true);
        await PostAsync(scope, time, "historical", BusinessDate.AddDays(-120), authority);

        await using CoreLedgerDbContext verification = scope.CreateContext();
        RecalculationHandoffRecord handoff = await verification.RecalculationHandoffs.SingleAsync(
            record => record.InstitutionId == scope.Institution.Value);
        Assert.Equal(120, handoff.AgeDays);
        Assert.Equal("backdate-approval", handoff.ApprovalReference);
        Assert.Equal(1, await verification.OutboxMessages.CountAsync(
            record => record.EventType == "financial_transaction_posted"));

        LedgerPostingException tooOld = await Assert.ThrowsAsync<LedgerPostingException>(
            () => PostAsync(scope, time, "too-old", BusinessDate.AddDays(-400), authority));
        Assert.Equal("value_date_too_old", tooOld.Code);
    }

    private static AccountingPeriodService Service(
        CoreLedgerDbContext context,
        CoreLedgerDatabaseScope scope,
        TimeProvider time) =>
        new(context, scope.NewTenant(), time);

    private static async Task<AccountingPeriodResult> CloseAsync(
        CoreLedgerDbContext context,
        CoreLedgerDatabaseScope scope,
        AccountingPeriodService periods) =>
        await periods.CloseAsync(
            new CloseAccountingPeriodCommand(
                scope.Institution,
                "legal-entity-1",
                2026,
                11,
                Actor,
                1,
                100m,
                100m,
                0,
                true,
                true,
                "close-approval",
                new string('a', 64),
                "close"),
            CancellationToken.None);

    private static async Task<object> CloseAsync(
        CoreLedgerDatabaseScope scope,
        TimeProvider time,
        string prefix)
    {
        try
        {
            await using CoreLedgerDbContext context = scope.CreateContext(scope.NewTenant());
            AccountingPeriodResult result = await Service(context, scope, time).CloseAsync(
                new CloseAccountingPeriodCommand(
                    scope.Institution,
                    "legal-entity-1",
                    2026,
                    11,
                    Actor,
                    1,
                    100m,
                    100m,
                    0,
                    true,
                    true,
                    "close-approval",
                    new string('a', 64),
                    prefix + "-close"),
                CancellationToken.None);
            return result;
        }
        catch (LedgerPostingException exception)
        {
            return exception;
        }
    }

    private static ReopenAccountingPeriodCommand ReopenCommand(
        CoreLedgerDatabaseScope scope,
        long expectedVersion,
        IReadOnlyList<AccountingPeriodApprovalInput> approvals) =>
        new(
            scope.Institution,
            "legal-entity-1",
            2026,
            11,
            Actor,
            expectedVersion,
            ["internal_transfer.v1"],
            approvals,
            CoreLedgerDatabaseScope.SeededAt.AddHours(1),
            "prior_period_correction",
            "reopen");

    private static async Task<LedgerPostingResult> PostAsync(
        CoreLedgerDatabaseScope scope,
        TimeProvider time,
        string prefix,
        DateOnly? valueDate = null,
        DateAuthority? authority = null)
    {
        string source = await OpenLedgerAccountAsync(scope, prefix + "-source", time);
        string recipient = await OpenLedgerAccountAsync(scope, prefix + "-recipient", time);
        await FundAsync(scope, source, 100m);

        FinancialDates dates = FinancialDates.Create(
            time.GetUtcNow(),
            BusinessDate,
            BusinessDate,
            valueDate ?? BusinessDate);
        string hash = CanonicalFinancialRequest.Create("""{"amount":75,"currency":"NGN"}""").Hash;
        FinancialTransaction transaction = FinancialTransaction.CreateReadyToPost(
            prefix + "-transaction",
            prefix + "-TX",
            scope.Institution.Value,
            "payments-vtu",
            prefix + "-source-reference",
            "internal_transfer.v1",
            hash,
            dates);
        AccountingScope accountingScope = new(scope.Institution.Value, "legal-entity-1", CurrencyCode.Parse("NGN"));
        JournalEntry journal = JournalEntry.Create(
            prefix + "-journal",
            transaction.Id,
            prefix + "-JRN",
            "mapping-version-1",
            accountingScope,
            dates,
            [
                JournalLine.Create(
                    prefix + "-line-1", 1, source, DebitCreditIndicator.Debit,
                    AccountingComponentType.Principal, Money.Create(75m, "NGN"), accountingScope),
                JournalLine.Create(
                    prefix + "-line-2", 2, recipient, DebitCreditIndicator.Credit,
                    AccountingComponentType.Principal, Money.Create(75m, "NGN"), accountingScope),
            ]);

        AmbientTenantContext tenant = scope.NewTenant();
        await using CoreLedgerDbContext context = scope.CreateContext(tenant);
        return await PostingService(context, tenant, time).PostAsync(
            new AtomicLedgerPostingCommand(
                PostingIdempotencyScope.Create(
                    scope.Institution.Value, "ledger.transaction.post", "payments-vtu", prefix + "-key"),
                transaction,
                journal,
                new LedgerPostingControlContext(
                    "api",
                    "internal_transfer",
                    new Dictionary<string, long>(StringComparer.Ordinal) { [source] = 0, [recipient] = 0 }),
                Actor.Value,
                "service",
                prefix + "-correlation",
                DateAuthority: authority),
            CancellationToken.None);
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
            .Where(record => record.AccountId == accountId)
            .Select(record => record.LedgerAccountId)
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

    private sealed class MutableTimeProvider : TimeProvider
    {
        internal MutableTimeProvider(DateTimeOffset now) => Now = now;

        internal DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
