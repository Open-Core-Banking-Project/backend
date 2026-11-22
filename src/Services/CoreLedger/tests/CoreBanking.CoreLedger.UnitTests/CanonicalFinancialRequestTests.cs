using System;
using CoreBanking.CoreLedger.Domain.Ledger;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class CanonicalFinancialRequestTests
{
    [Fact]
    public void Property_order_whitespace_and_equivalent_decimal_forms_have_one_hash()
    {
        CanonicalFinancialRequest first = CanonicalFinancialRequest.Create(
            """ { "amount": 100.00, "currency": "NGN", "components": [1.0, 2] } """);
        CanonicalFinancialRequest second = CanonicalFinancialRequest.Create(
            """{"components":[1,2.0],"currency":"NGN","amount":100}""");

        Assert.Equal(first.CanonicalJson, second.CanonicalJson);
        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal(64, first.Hash.Length);
    }

    [Fact]
    public void Array_order_remains_material_to_the_request_hash()
    {
        CanonicalFinancialRequest first = CanonicalFinancialRequest.Create("""{"lines":["debit","credit"]}""");
        CanonicalFinancialRequest second = CanonicalFinancialRequest.Create("""{"lines":["credit","debit"]}""");

        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void Duplicate_property_is_refused_instead_of_becoming_ambiguous()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => CanonicalFinancialRequest.Create("""{"amount":100,"amount":200}"""));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Number_outside_decimal_range_is_refused()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => CanonicalFinancialRequest.Create("""{"amount":1e100}"""));

        Assert.Contains("decimal range", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
