using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreBanking.CoreLedger.Domain.Ledger;

public enum LedgerAccountRole
{
    CustomerDeposit,
    CashSettlement,
    InternalTransferClearing,
    FeeIncome,
    CommissionPayable,
    TaxPayable,
    InterestExpense,
    PenaltyIncome,
    Suspense,
}

public enum AccountingComponentType
{
    Principal,
    Fee,
    Tax,
    Commission,
    Penalty,
    Interest,
    Waiver,
    Rounding,
}

public enum DebitCreditIndicator
{
    Debit,
    Credit,
}

public sealed record AccountingScope(string InstitutionId, string LegalEntityCode, CurrencyCode Currency);

public sealed record LedgerChartAccount(
    string AccountCode,
    LedgerAccountRole Role,
    DebitCreditIndicator NormalSide,
    CurrencyCode Currency,
    bool Active);

public sealed class LedgerChart
{
    private readonly LedgerChartAccount[] _accounts;

    public LedgerChart(string versionId, string institutionId, string legalEntityCode, IEnumerable<LedgerChartAccount> accounts)
    {
        VersionId = Required(versionId, nameof(versionId));
        InstitutionId = Required(institutionId, nameof(institutionId));
        LegalEntityCode = Required(legalEntityCode, nameof(legalEntityCode));
        _accounts = accounts?.ToArray() ?? throw new ArgumentNullException(nameof(accounts));
        if (_accounts.Length == 0)
        {
            throw new ArgumentException("A chart must contain at least one account.", nameof(accounts));
        }
    }

    public string VersionId { get; }

    public string InstitutionId { get; }

    public string LegalEntityCode { get; }

    public LedgerChartAccount Resolve(LedgerAccountRole role, CurrencyCode currency)
    {
        LedgerChartAccount[] matches = _accounts
            .Where(account => account.Role == role && account.Currency == currency && account.Active)
            .ToArray();

        return matches.Length switch
        {
            0 => throw new LedgerMappingException("ledger_role_unknown"),
            1 => matches[0],
            _ => throw new LedgerMappingException("ledger_role_ambiguous"),
        };
    }

    private static string Required(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }
}

public sealed record MappingComponentRule(AccountingComponentType Component, bool Required);

public sealed record MappingLineTemplate(
    AccountingComponentType Component,
    LedgerAccountRole Role,
    DebitCreditIndicator Direction);

public sealed record SimulatedJournalLine(
    AccountingComponentType Component,
    string LedgerAccountCode,
    DebitCreditIndicator Direction,
    Money Amount,
    AccountingScope Scope);

public sealed record MappingSimulation(IReadOnlyCollection<SimulatedJournalLine> Lines, decimal TotalDebits, decimal TotalCredits);

public sealed class AccountingEventMapping
{
    private readonly IReadOnlyDictionary<AccountingComponentType, MappingComponentRule> _components;
    private readonly MappingLineTemplate[] _templates;

    public AccountingEventMapping(
        string eventCode,
        string versionId,
        string institutionId,
        string legalEntityCode,
        IEnumerable<MappingComponentRule> components,
        IEnumerable<MappingLineTemplate> templates)
    {
        EventCode = Required(eventCode, nameof(eventCode));
        VersionId = Required(versionId, nameof(versionId));
        InstitutionId = Required(institutionId, nameof(institutionId));
        LegalEntityCode = Required(legalEntityCode, nameof(legalEntityCode));
        _components = (components ?? throw new ArgumentNullException(nameof(components)))
            .ToDictionary(component => component.Component);
        _templates = (templates ?? throw new ArgumentNullException(nameof(templates))).ToArray();

        if (_components.Count == 0 || _templates.Length < 2)
        {
            throw new ArgumentException("A mapping needs components and at least two line templates.");
        }

        if (_templates.Any(template => !_components.ContainsKey(template.Component)))
        {
            throw new LedgerMappingException("accounting_component_invalid");
        }
    }

    public string EventCode { get; }

    public string VersionId { get; }

    public string InstitutionId { get; }

    public string LegalEntityCode { get; }

    public MappingSimulation Simulate(LedgerChart chart, IReadOnlyDictionary<AccountingComponentType, Money> supplied)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(supplied);

        if (!string.Equals(chart.InstitutionId, InstitutionId, StringComparison.Ordinal)
            || !string.Equals(chart.LegalEntityCode, LegalEntityCode, StringComparison.Ordinal))
        {
            throw new LedgerMappingException("accounting_scope_mismatch");
        }

        if (_components.Values.Any(rule => rule.Required && !supplied.ContainsKey(rule.Component))
            || supplied.Keys.Any(component => !_components.ContainsKey(component))
            || supplied.Values.Any(amount => amount.Amount <= 0))
        {
            throw new LedgerMappingException("accounting_component_invalid");
        }

        CurrencyCode[] currencies = supplied.Values.Select(amount => amount.Currency).Distinct().ToArray();
        if (currencies.Length != 1)
        {
            throw new LedgerMappingException("accounting_currency_mismatch");
        }

        CurrencyCode currency = currencies[0];
        AccountingScope scope = new(InstitutionId, LegalEntityCode, currency);
        List<SimulatedJournalLine> lines = new();

        foreach (MappingLineTemplate template in _templates.Where(template => supplied.ContainsKey(template.Component)))
        {
            LedgerChartAccount account = chart.Resolve(template.Role, currency);
            lines.Add(new SimulatedJournalLine(template.Component, account.AccountCode, template.Direction, supplied[template.Component], scope));
        }

        decimal debits = lines.Where(line => line.Direction == DebitCreditIndicator.Debit).Sum(line => line.Amount.Amount);
        decimal credits = lines.Where(line => line.Direction == DebitCreditIndicator.Credit).Sum(line => line.Amount.Amount);
        if (debits != credits)
        {
            throw new LedgerMappingException("journal_unbalanced");
        }

        return new MappingSimulation(lines, debits, credits);
    }

    private static string Required(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }
}

public sealed class LedgerMappingException : InvalidOperationException
{
    public LedgerMappingException(string code)
        : base(code) => Code = code;

    public string Code { get; }
}

