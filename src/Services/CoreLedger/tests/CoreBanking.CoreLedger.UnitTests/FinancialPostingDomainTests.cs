using System;
using CoreBanking.CoreLedger.Domain.Ledger;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class FinancialPostingDomainTests
{
    [Fact]
    public void Balanced_multi_line_journal_is_accepted_with_explicit_components()
    {
        AccountingScope scope = Scope();

        JournalEntry journal = JournalEntry.Create(
            "journal-1",
            "transaction-1",
            "JRN-1",
            "mapping-1",
            scope,
            Dates(),
            [
                Line("line-1", 1, "customer", DebitCreditIndicator.Debit, AccountingComponentType.Principal, 105m, scope),
                Line("line-2", 2, "settlement", DebitCreditIndicator.Credit, AccountingComponentType.Principal, 100m, scope),
                Line("line-3", 3, "fee-income", DebitCreditIndicator.Credit, AccountingComponentType.Fee, 5m, scope, "published_tariff"),
            ]);

        Assert.Equal(105m, journal.TotalDebits);
        Assert.Equal(105m, journal.TotalCredits);
        Assert.Equal(3, journal.Lines.Count);
        Assert.Equal("mapping-1", journal.MappingVersionId);
    }

    [Fact]
    public void Unbalanced_journal_is_refused()
    {
        AccountingScope scope = Scope();

        LedgerPostingException exception = Assert.Throws<LedgerPostingException>(() => JournalEntry.Create(
            "journal-1",
            "transaction-1",
            "JRN-1",
            "mapping-1",
            scope,
            Dates(),
            [
                Line("line-1", 1, "cash", DebitCreditIndicator.Debit, AccountingComponentType.Principal, 100m, scope),
                Line("line-2", 2, "customer", DebitCreditIndicator.Credit, AccountingComponentType.Principal, 99m, scope),
            ]));

        Assert.Equal("journal_unbalanced", exception.Code);
    }

    [Fact]
    public void Mixed_scope_or_currency_is_refused()
    {
        AccountingScope scope = Scope();
        AccountingScope other = new("institution-1", "legal-entity-2", CurrencyCode.Parse("USD"));

        LedgerPostingException exception = Assert.Throws<LedgerPostingException>(() => JournalEntry.Create(
            "journal-1",
            "transaction-1",
            "JRN-1",
            "mapping-1",
            scope,
            Dates(),
            [
                Line("line-1", 1, "cash", DebitCreditIndicator.Debit, AccountingComponentType.Principal, 100m, scope),
                Line("line-2", 2, "customer", DebitCreditIndicator.Credit, AccountingComponentType.Principal, 100m, other, currency: "USD"),
            ]));

        Assert.Equal("accounting_scope_mismatch", exception.Code);
    }

    [Fact]
    public void Non_principal_component_requires_reason()
    {
        LedgerPostingException exception = Assert.Throws<LedgerPostingException>(() => JournalLine.Create(
            "line-1",
            1,
            "fee-income",
            DebitCreditIndicator.Credit,
            AccountingComponentType.Fee,
            Money.Create(5m, "NGN"),
            Scope()));

        Assert.Equal("accounting_component_reason_required", exception.Code);
    }

    [Fact]
    public void Posted_transaction_requires_bound_journal_and_cannot_post_twice()
    {
        FinancialTransaction transaction = Transaction();
        JournalEntry journal = JournalEntry.Create(
            "journal-1",
            transaction.Id,
            "JRN-1",
            "mapping-1",
            Scope(),
            Dates(),
            [
                Line("line-1", 1, "cash", DebitCreditIndicator.Debit, AccountingComponentType.Principal, 100m, Scope()),
                Line("line-2", 2, "customer", DebitCreditIndicator.Credit, AccountingComponentType.Principal, 100m, Scope()),
            ]);

        transaction.MarkPosted([journal]);

        Assert.Equal(FinancialTransactionStatus.Posted, transaction.Status);
        Assert.Single(transaction.Journals);
        LedgerPostingException exception = Assert.Throws<LedgerPostingException>(() => transaction.MarkPosted([journal]));
        Assert.Equal("financial_transaction_state_invalid", exception.Code);
    }

    [Fact]
    public void Transaction_cannot_be_posted_without_a_journal()
    {
        FinancialTransaction transaction = Transaction();

        LedgerPostingException exception = Assert.Throws<LedgerPostingException>(() => transaction.MarkPosted([]));

        Assert.Equal("financial_transaction_journal_required", exception.Code);
    }

    private static FinancialTransaction Transaction() => FinancialTransaction.CreateReadyToPost(
        "transaction-1",
        "TXN-1",
        "institution-1",
        "payments-vtu",
        "payment-1",
        "customer_deposit.v1",
        new string('a', 64),
        Dates());

    private static JournalLine Line(
        string id,
        int sequence,
        string account,
        DebitCreditIndicator direction,
        AccountingComponentType component,
        decimal amount,
        AccountingScope scope,
        string? reason = null,
        string currency = "NGN") =>
        JournalLine.Create(id, sequence, account, direction, component, Money.Create(amount, currency), scope, reason);

    private static AccountingScope Scope() => new("institution-1", "legal-entity-1", CurrencyCode.Parse("NGN"));

    private static FinancialDates Dates() => FinancialDates.Create(
        new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero),
        new DateOnly(2026, 11, 21),
        new DateOnly(2026, 11, 21),
        new DateOnly(2026, 11, 21));
}
