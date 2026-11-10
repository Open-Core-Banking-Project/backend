using System;
using System.Linq;
using System.Text.Json;
using CoreBanking.CoreLedger.Contracts;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class ProductsAccountsContractTests
{
    [Fact]
    public void Account_opening_request_cannot_assign_tenant_status_approval_or_ledger_state()
    {
        string[] properties = typeof(CreateAccountApplicationRequestV1)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("InstitutionId", properties);
        Assert.DoesNotContain("Status", properties);
        Assert.DoesNotContain("ApprovalReference", properties);
        Assert.DoesNotContain("AccountNumber", properties);
        Assert.DoesNotContain("LedgerAccountReference", properties);
        Assert.DoesNotContain("LedgerBalance", properties);
        Assert.DoesNotContain("AvailableBalance", properties);
    }

    [Fact]
    public void Account_summary_keeps_ledger_values_distinct_and_source_labelled()
    {
        AccountSummaryResponseV1 response = new(
            "institution-1",
            "account-1",
            "******1234",
            "customer-1",
            "product-1",
            "product-version-3",
            "branch-1",
            "NGN",
            "active",
            7,
            "available",
            new AccountBalanceSummaryV1(
                new MonetaryAmountV1(1500.00m, "NGN"),
                new MonetaryAmountV1(1200.00m, "NGN"),
                new MonetaryAmountV1(100.00m, "NGN"),
                new MonetaryAmountV1(200.00m, "NGN"),
                [new AccountBlockSummaryV1("block-1", "debits", "active")],
                new BalanceSourceV1(
                    "ledger-account-ref",
                    "balance-version-8",
                    new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero))));

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(response));
        JsonElement balances = document.RootElement.GetProperty("Balances");

        Assert.Equal(1500.00m, balances.GetProperty("LedgerBalance").GetProperty("Amount").GetDecimal());
        Assert.Equal(1200.00m, balances.GetProperty("AvailableBalance").GetProperty("Amount").GetDecimal());
        Assert.Equal(100.00m, balances.GetProperty("Holds").GetProperty("Amount").GetDecimal());
        Assert.Equal(200.00m, balances.GetProperty("Liens").GetProperty("Amount").GetDecimal());
        Assert.Equal("balance-version-8", balances.GetProperty("Source").GetProperty("SnapshotVersion").GetString());
        Assert.Single(balances.GetProperty("Blocks").EnumerateArray());
    }

    [Fact]
    public void Control_requests_cannot_assign_approval_or_effective_status()
    {
        string[] restrictionProperties = typeof(CreateAccountRestrictionRequestV1)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        string[] lienProperties = typeof(CreateAccountLienRequestV1)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("InstitutionId", restrictionProperties);
        Assert.DoesNotContain("Status", restrictionProperties);
        Assert.DoesNotContain("ApprovalReference", restrictionProperties);
        Assert.DoesNotContain("InstitutionId", lienProperties);
        Assert.DoesNotContain("Status", lienProperties);
        Assert.DoesNotContain("ApprovalReference", lienProperties);
    }

    [Fact]
    public void Statement_event_hands_off_an_opaque_ledger_query_without_financial_history()
    {
        StatementRequestedV1 message = new(
            "institution-1",
            "statement-request-1",
            "account-1",
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31),
            "configured-format-code",
            "customer",
            "ledger-query-ref",
            "correlation-1",
            new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(message));
        JsonElement root = document.RootElement;

        Assert.Equal("ledger-query-ref", root.GetProperty("LedgerQueryReference").GetString());
        Assert.False(root.TryGetProperty("Transactions", out _));
        Assert.False(root.TryGetProperty("Entries", out _));
        Assert.False(root.TryGetProperty("DownloadUrl", out _));
        Assert.False(root.TryGetProperty("DocumentContent", out _));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Account_contracts_are_transport_only_and_do_not_reference_implementation_layers()
    {
        string[] forbiddenNamespaces =
        [
            ".Domain",
            ".Application",
            ".Infrastructure",
            ".Api",
            ".Workers",
        ];

        Type[] contractTypes =
        [
            typeof(CreateAccountApplicationRequestV1),
            typeof(AccountSummaryResponseV1),
            typeof(CreateAccountRestrictionRequestV1),
            typeof(AccountControlDecisionV1),
            typeof(CreateStatementRequestV1),
            typeof(StatementRequestedV1),
        ];

        foreach (Type contractType in contractTypes)
        {
            foreach (Type referencedType in contractType.GetProperties().Select(property => property.PropertyType))
            {
                Assert.DoesNotContain(
                    forbiddenNamespaces,
                    forbiddenNamespace => (referencedType.Namespace ?? string.Empty).Contains(forbiddenNamespace, StringComparison.Ordinal));
            }
        }
    }
}
