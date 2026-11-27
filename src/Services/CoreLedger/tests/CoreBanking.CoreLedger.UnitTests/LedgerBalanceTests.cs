using System;
using CoreBanking.CoreLedger.Domain.Ledger;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class LedgerBalanceTests
{
    [Fact]
    public void Credit_normal_deposit_credit_increases_visible_balance()
    {
        LedgerBalance balance = Balance(DebitCreditIndicator.Credit);

        LedgerMovement movement = balance.Apply(Line(DebitCreditIndicator.Credit, 125m), "movement-1", Instant());

        Assert.Equal(-125m, balance.NetBalance);
        Assert.Equal(125m, balance.VisibleBalance);
        Assert.Equal(0m, movement.VisibleBalanceBefore);
        Assert.Equal(125m, movement.VisibleBalanceAfter);
        Assert.Equal(2, balance.Version);
    }

    [Fact]
    public void Debit_normal_debit_increases_visible_balance()
    {
        LedgerBalance balance = Balance(DebitCreditIndicator.Debit);

        balance.Apply(Line(DebitCreditIndicator.Debit, 40m), "movement-1", Instant());

        Assert.Equal(40m, balance.NetBalance);
        Assert.Equal(40m, balance.VisibleBalance);
    }

    [Fact]
    public void Available_funds_keeps_deductions_distinct_and_reports_deficit()
    {
        LedgerBalance balance = LedgerBalance.Restore(
            "account-1", "NGN", DebitCreditIndicator.Credit,
            debitTotal: 0m, creditTotal: 100m, creditLimit: 0m,
            holdRemaining: 30m, pendingAuthorised: 25m, version: 4);

        AvailableFundsPosition available = balance.Available(outstandingLiens: 60m);

        Assert.Equal(0m, available.Available);
        Assert.Equal(15m, available.Deficit);
        Assert.Equal(30m, available.HoldRemaining);
        Assert.Equal(60m, available.OutstandingLiens);
        Assert.Equal(25m, available.PendingAuthorised);
    }

    [Fact]
    public void Movement_rejects_account_mismatch()
    {
        LedgerBalance balance = Balance(DebitCreditIndicator.Credit);

        LedgerPostingException account = Assert.Throws<LedgerPostingException>(() => balance.Apply(
            JournalLine.Create("line-1", 1, "other", DebitCreditIndicator.Credit, AccountingComponentType.Principal,
                Money.Create(1m, "NGN"), Scope()),
            "movement-1",
            Instant()));

        Assert.Equal("ledger_balance_account_mismatch", account.Code);
    }

    private static LedgerBalance Balance(DebitCreditIndicator normalSide) =>
        LedgerBalance.Restore("account-1", "NGN", normalSide, 0m, 0m, 0m, 0m, 0m, 1);

    private static JournalLine Line(DebitCreditIndicator direction, decimal amount) =>
        JournalLine.Create(
            "line-1", 1, "account-1", direction, AccountingComponentType.Principal,
            Money.Create(amount, "NGN"), Scope());

    private static AccountingScope Scope() =>
        new("institution-1", "legal-entity-1", CurrencyCode.Parse("NGN"));

    private static DateTimeOffset Instant() =>
        new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
}
