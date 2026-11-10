using System;
using System.Linq;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Ledger;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class FinancialPostingPrimitiveTests
{
    [Fact]
    public void Money_keeps_decimal_amount_and_explicit_uppercase_currency()
    {
        Money money = Money.Create(125.4321m, "NGN");

        Assert.Equal(125.4321m, money.Amount);
        Assert.Equal("NGN", money.Currency.Value);
        Assert.Equal("125.4321 NGN", money.ToString());
    }

    [Theory]
    [InlineData("ngn")]
    [InlineData("NG")]
    [InlineData("NGN1")]
    [InlineData(" N G ")]
    public void Money_refuses_an_invalid_currency(string currency)
    {
        Assert.Throws<ArgumentException>(() => Money.Create(1m, currency));
    }

    [Fact]
    public void Money_refuses_implicit_rounding_beyond_the_approved_scale()
    {
        Assert.Throws<ArgumentException>(() => Money.Create(1.00001m, "NGN"));
    }

    [Fact]
    public void Financial_dates_do_not_substitute_one_clock_for_another()
    {
        FinancialDates dates = FinancialDates.Create(
            new DateTimeOffset(2026, 11, 9, 18, 30, 0, TimeSpan.FromHours(1)),
            new DateOnly(2026, 11, 10),
            new DateOnly(2026, 11, 9),
            new DateOnly(2026, 11, 7));

        Assert.Equal(new DateOnly(2026, 11, 10), dates.BusinessDate);
        Assert.Equal(new DateOnly(2026, 11, 9), dates.PostingDate);
        Assert.Equal(new DateOnly(2026, 11, 7), dates.ValueDate);
        Assert.Equal(TimeSpan.FromHours(1), dates.TransactionTimestamp.Offset);
    }

    [Fact]
    public void References_trim_edges_but_preserve_case()
    {
        FinancialReference upper = FinancialReference.Parse(" ABC-1 ");
        FinancialReference lower = FinancialReference.Parse("abc-1");

        Assert.Equal("ABC-1", upper.Value);
        Assert.Equal("abc-1", lower.Value);
        Assert.NotEqual(upper, lower);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("line\nbreak")]
    [InlineData("reference-é")]
    public void References_refuse_empty_control_or_non_ascii_values(string value)
    {
        Assert.Throws<ArgumentException>(() => FinancialReference.Parse(value));
    }

    [Fact]
    public void External_reference_identity_includes_producer_and_type()
    {
        ExternalReference first = ExternalReference.Create("provider-a", ExternalReferenceType.ProviderReference, "ABC-1");
        ExternalReference second = ExternalReference.Create("provider-b", ExternalReferenceType.ProviderReference, "ABC-1");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Idempotency_scope_contains_every_approved_uniqueness_dimension()
    {
        PostingIdempotencyScope scope = PostingIdempotencyScope.Create(
            "institution-1",
            "ledger.transaction.post",
            "payments-service",
            "request-1");

        Assert.Equal("institution-1", scope.InstitutionId.Value);
        Assert.Equal("ledger.transaction.post", scope.Operation.Value);
        Assert.Equal("payments-service", scope.Initiator.Value);
        Assert.Equal("request-1", scope.Key.Value);
    }

    [Fact]
    public void Posting_contracts_cannot_assign_ledger_ids_balances_or_journal_lines()
    {
        string[] properties = typeof(FinancialReferencesV1).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("InstitutionId", properties);
        Assert.DoesNotContain("FinancialTransactionId", properties);
        Assert.DoesNotContain("JournalReference", properties);
        Assert.DoesNotContain("JournalLines", properties);
        Assert.DoesNotContain("Balance", properties);
    }
}

