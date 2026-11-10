using System;
using System.Collections.Generic;
using CoreBanking.CoreLedger.Domain.Ledger;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class AccountingMappingTests
{
    [Fact]
    public void Chart_configuration_is_canonical_and_rejects_ambiguous_role_resolution()
    {
        const string valid = """
            {"legalEntityCode":"legal-entity-1","accounts":[{"code":"2100","role":"customerDeposit","normalSide":"credit","currency":"NGN","status":"active"}]}
            """;
        const string ambiguous = """
            {"legalEntityCode":"legal-entity-1","accounts":[{"code":"2100","role":"customerDeposit","normalSide":"credit","currency":"NGN","status":"active"},{"code":"2101","role":"customerDeposit","normalSide":"credit","currency":"NGN","status":"active"}]}
            """;

        Assert.Equal(LedgerConfigurationContent.ValidateChart(valid).Hash, LedgerConfigurationContent.ValidateChart(valid).Hash);
        Assert.Throws<ArgumentException>(() => LedgerConfigurationContent.ValidateChart(ambiguous));
    }

    [Fact]
    public void Published_chart_resolves_the_customer_deposit_classification()
    {
        const string content = """
            {"legalEntityCode":"legal-entity-1","accounts":[{"code":"2100","role":"customerDeposit","normalSide":"credit","currency":"NGN","status":"active"}]}
            """;

        LedgerAccountClassification result = LedgerConfigurationContent.ResolveCustomerDeposit(content, "NGN");

        Assert.Equal("legal-entity-1", result.LegalEntityCode);
        Assert.Equal("2100", result.GlAccountCode);
    }

    [Fact]
    public void Mapping_configuration_requires_each_component_template_to_balance()
    {
        const string valid = """
            {"eventCode":"customer_deposit.v1","legalEntityCode":"legal-entity-1","components":[{"type":"principal","required":true}],"lines":[{"component":"principal","role":"cashSettlement","direction":"debit"},{"component":"principal","role":"customerDeposit","direction":"credit"}]}
            """;
        const string unbalanced = """
            {"eventCode":"customer_deposit.v1","legalEntityCode":"legal-entity-1","components":[{"type":"principal","required":true}],"lines":[{"component":"principal","role":"cashSettlement","direction":"debit"},{"component":"principal","role":"customerDeposit","direction":"debit"}]}
            """;

        Assert.Equal(LedgerConfigurationContent.MappingArea, "ledger.mapping");
        Assert.NotEmpty(LedgerConfigurationContent.ValidateMapping(valid).Hash);
        Assert.Throws<ArgumentException>(() => LedgerConfigurationContent.ValidateMapping(unbalanced));
    }

    [Fact]
    public void Ledger_configuration_rejects_unknown_fields_and_component_types()
    {
        const string unknownField = """
            {"legalEntityCode":"legal-entity-1","accounts":[],"fallbackAccount":"suspense"}
            """;
        const string unknownComponent = """
            {"eventCode":"customer_deposit.v1","legalEntityCode":"legal-entity-1","components":[{"type":"crypto","required":true}],"lines":[{"component":"crypto","role":"cashSettlement","direction":"debit"},{"component":"crypto","role":"customerDeposit","direction":"credit"}]}
            """;

        Assert.Throws<ArgumentException>(() => LedgerConfigurationContent.ValidateChart(unknownField));
        Assert.Throws<ArgumentException>(() => LedgerConfigurationContent.ValidateMapping(unknownComponent));
    }

    [Fact]
    public void Deposit_mapping_simulates_an_exact_balanced_journal()
    {
        LedgerChart chart = Chart(
            Account("1100", LedgerAccountRole.CashSettlement, DebitCreditIndicator.Debit),
            Account("2100", LedgerAccountRole.CustomerDeposit, DebitCreditIndicator.Credit));
        AccountingEventMapping mapping = Mapping(
            new MappingComponentRule(AccountingComponentType.Principal, true),
            new MappingLineTemplate(AccountingComponentType.Principal, LedgerAccountRole.CashSettlement, DebitCreditIndicator.Debit),
            new MappingLineTemplate(AccountingComponentType.Principal, LedgerAccountRole.CustomerDeposit, DebitCreditIndicator.Credit));

        MappingSimulation result = mapping.Simulate(chart, Components((AccountingComponentType.Principal, 100m, "NGN")));

        Assert.Equal(100m, result.TotalDebits);
        Assert.Equal(100m, result.TotalCredits);
        Assert.Equal(2, result.Lines.Count);
        Assert.All(result.Lines, line => Assert.Equal("legal-entity-1", line.Scope.LegalEntityCode));
    }

    [Fact]
    public void Missing_role_refuses_without_a_suspense_fallback()
    {
        LedgerChart chart = Chart(Account("1100", LedgerAccountRole.CashSettlement, DebitCreditIndicator.Debit));
        AccountingEventMapping mapping = DepositMapping();

        LedgerMappingException exception = Assert.Throws<LedgerMappingException>(
            () => mapping.Simulate(chart, Components((AccountingComponentType.Principal, 100m, "NGN"))));

        Assert.Equal("ledger_role_unknown", exception.Code);
    }

    [Fact]
    public void Ambiguous_role_refuses()
    {
        LedgerChart chart = Chart(
            Account("1100", LedgerAccountRole.CashSettlement, DebitCreditIndicator.Debit),
            Account("1101", LedgerAccountRole.CashSettlement, DebitCreditIndicator.Debit),
            Account("2100", LedgerAccountRole.CustomerDeposit, DebitCreditIndicator.Credit));

        LedgerMappingException exception = Assert.Throws<LedgerMappingException>(
            () => DepositMapping().Simulate(chart, Components((AccountingComponentType.Principal, 100m, "NGN"))));

        Assert.Equal("ledger_role_ambiguous", exception.Code);
    }

    [Fact]
    public void Undeclared_or_missing_component_refuses()
    {
        LedgerChart chart = Chart(
            Account("1100", LedgerAccountRole.CashSettlement, DebitCreditIndicator.Debit),
            Account("2100", LedgerAccountRole.CustomerDeposit, DebitCreditIndicator.Credit));
        AccountingEventMapping mapping = DepositMapping();

        Assert.Equal("accounting_component_invalid", Assert.Throws<LedgerMappingException>(() => mapping.Simulate(chart, Components((AccountingComponentType.Fee, 1m, "NGN")))).Code);
        Assert.Equal("accounting_component_invalid", Assert.Throws<LedgerMappingException>(() => mapping.Simulate(chart, new Dictionary<AccountingComponentType, Money>())).Code);
    }

    [Fact]
    public void Cross_currency_components_refuse()
    {
        LedgerChart chart = Chart(
            Account("2100", LedgerAccountRole.CustomerDeposit, DebitCreditIndicator.Debit),
            Account("4100", LedgerAccountRole.FeeIncome, DebitCreditIndicator.Credit),
            Account("2200", LedgerAccountRole.TaxPayable, DebitCreditIndicator.Credit));
        AccountingEventMapping mapping = Mapping(
            new[]
            {
                new MappingComponentRule(AccountingComponentType.Fee, true),
                new MappingComponentRule(AccountingComponentType.Tax, true),
            },
            new MappingLineTemplate(AccountingComponentType.Fee, LedgerAccountRole.CustomerDeposit, DebitCreditIndicator.Debit),
            new MappingLineTemplate(AccountingComponentType.Fee, LedgerAccountRole.FeeIncome, DebitCreditIndicator.Credit),
            new MappingLineTemplate(AccountingComponentType.Tax, LedgerAccountRole.CustomerDeposit, DebitCreditIndicator.Debit),
            new MappingLineTemplate(AccountingComponentType.Tax, LedgerAccountRole.TaxPayable, DebitCreditIndicator.Credit));

        LedgerMappingException exception = Assert.Throws<LedgerMappingException>(() => mapping.Simulate(
            chart,
            Components((AccountingComponentType.Fee, 10m, "NGN"), (AccountingComponentType.Tax, 1m, "USD"))));

        Assert.Equal("accounting_currency_mismatch", exception.Code);
    }

    [Fact]
    public void Lines_cannot_net_across_legal_entities()
    {
        LedgerChart otherEntity = new("chart-1", "institution-1", "legal-entity-2", [
            Account("1100", LedgerAccountRole.CashSettlement, DebitCreditIndicator.Debit),
            Account("2100", LedgerAccountRole.CustomerDeposit, DebitCreditIndicator.Credit)]);

        LedgerMappingException exception = Assert.Throws<LedgerMappingException>(() => DepositMapping().Simulate(
            otherEntity,
            Components((AccountingComponentType.Principal, 100m, "NGN"))));

        Assert.Equal("accounting_scope_mismatch", exception.Code);
    }

    [Fact]
    public void Unbalanced_template_refuses_simulation()
    {
        LedgerChart chart = Chart(
            Account("1100", LedgerAccountRole.CashSettlement, DebitCreditIndicator.Debit),
            Account("2100", LedgerAccountRole.CustomerDeposit, DebitCreditIndicator.Credit));
        AccountingEventMapping mapping = Mapping(
            new[]
            {
                new MappingComponentRule(AccountingComponentType.Principal, true),
                new MappingComponentRule(AccountingComponentType.Fee, false),
            },
            new MappingLineTemplate(AccountingComponentType.Principal, LedgerAccountRole.CashSettlement, DebitCreditIndicator.Debit),
            new MappingLineTemplate(AccountingComponentType.Principal, LedgerAccountRole.CustomerDeposit, DebitCreditIndicator.Credit),
            new MappingLineTemplate(AccountingComponentType.Fee, LedgerAccountRole.CashSettlement, DebitCreditIndicator.Debit));

        LedgerMappingException exception = Assert.Throws<LedgerMappingException>(() => mapping.Simulate(
            chart,
            Components((AccountingComponentType.Principal, 100m, "NGN"), (AccountingComponentType.Fee, 5m, "NGN"))));

        Assert.Equal("journal_unbalanced", exception.Code);
    }

    private static AccountingEventMapping DepositMapping() => Mapping(
        new MappingComponentRule(AccountingComponentType.Principal, true),
        new MappingLineTemplate(AccountingComponentType.Principal, LedgerAccountRole.CashSettlement, DebitCreditIndicator.Debit),
        new MappingLineTemplate(AccountingComponentType.Principal, LedgerAccountRole.CustomerDeposit, DebitCreditIndicator.Credit));

    private static AccountingEventMapping Mapping(MappingComponentRule component, params MappingLineTemplate[] templates) => Mapping([component], templates);

    private static AccountingEventMapping Mapping(IEnumerable<MappingComponentRule> components, params MappingLineTemplate[] templates) =>
        new("customer_deposit.v1", "mapping-1", "institution-1", "legal-entity-1", components, templates);

    private static LedgerChart Chart(params LedgerChartAccount[] accounts) => new("chart-1", "institution-1", "legal-entity-1", accounts);

    private static LedgerChartAccount Account(string code, LedgerAccountRole role, DebitCreditIndicator side) =>
        new(code, role, side, CurrencyCode.Parse("NGN"), true);

    private static Dictionary<AccountingComponentType, Money> Components(params (AccountingComponentType Type, decimal Amount, string Currency)[] values)
    {
        Dictionary<AccountingComponentType, Money> result = new();
        foreach ((AccountingComponentType type, decimal amount, string currency) in values)
        {
            result.Add(type, Money.Create(amount, currency));
        }

        return result;
    }
}

