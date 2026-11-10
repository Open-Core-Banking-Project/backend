using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CoreBanking.CoreLedger.Domain.Institutions;

namespace CoreBanking.CoreLedger.Domain.Ledger;

public static class LedgerConfigurationContent
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public const string ChartArea = "ledger.chart";
    public const string ChartSchema = "ledger.chart.v1";
    public const string MappingArea = "ledger.mapping";
    public const string MappingSchema = "ledger.mapping.v1";

    public static ConfigurationContent ValidateChart(string json)
    {
        ChartPayload payload = Deserialize<ChartPayload>(json);
        Required(payload.LegalEntityCode, "legalEntityCode");
        if (payload.Accounts is null || payload.Accounts.Length == 0)
        {
            throw new ArgumentException("A chart requires at least one account.", nameof(json));
        }

        HashSet<string> codes = new(StringComparer.Ordinal);
        HashSet<string> resolutions = new(StringComparer.Ordinal);
        foreach (ChartAccountPayload account in payload.Accounts)
        {
            Required(account.Code, "code");
            LedgerAccountRole role = ParseEnum<LedgerAccountRole>(account.Role, "role");
            ParseEnum<DebitCreditIndicator>(account.NormalSide, "normalSide");
            CurrencyCode currency = CurrencyCode.Parse(account.Currency);
            if (account.Status is not ("active" or "inactive"))
            {
                throw new ArgumentException("A chart account status must be active or inactive.", nameof(json));
            }
            if (!codes.Add(account.Code.Trim()))
            {
                throw new ArgumentException("A chart account code must be unique.", nameof(json));
            }

            if (!resolutions.Add(string.Concat(role, "|", currency.Value)))
            {
                throw new ArgumentException("A role and currency must resolve to exactly one chart account.", nameof(json));
            }
        }

        return ConfigurationContent.Create(json);
    }

    public static LedgerAccountClassification ResolveCustomerDeposit(string json, string currencyCode)
    {
        ConfigurationContent content = ValidateChart(json);
        ChartPayload payload = Deserialize<ChartPayload>(content.CanonicalJson);
        CurrencyCode currency = CurrencyCode.Parse(currencyCode);
        ChartAccountPayload[] matches = payload.Accounts.Where(account =>
            ParseEnum<LedgerAccountRole>(account.Role, "role") == LedgerAccountRole.CustomerDeposit
            && CurrencyCode.Parse(account.Currency) == currency
            && string.Equals(account.Status, "active", StringComparison.Ordinal)).ToArray();

        return matches.Length switch
        {
            0 => throw new LedgerMappingException("ledger_role_unknown"),
            1 => new LedgerAccountClassification(payload.LegalEntityCode.Trim(), matches[0].Code.Trim()),
            _ => throw new LedgerMappingException("ledger_role_ambiguous"),
        };
    }

    public static ConfigurationContent ValidateMapping(string json)
    {
        MappingPayload payload = Deserialize<MappingPayload>(json);
        Required(payload.EventCode, "eventCode");
        Required(payload.LegalEntityCode, "legalEntityCode");
        if (!payload.EventCode.EndsWith(".v1", StringComparison.Ordinal))
        {
            throw new ArgumentException("A Phase 1 event code must carry the .v1 version.", nameof(json));
        }

        if (payload.Components is null || payload.Components.Length == 0
            || payload.Lines is null || payload.Lines.Length < 2)
        {
            throw new ArgumentException("A mapping requires components and line templates.", nameof(json));
        }

        HashSet<AccountingComponentType> components = payload.Components
            .Select(component => ParseEnum<AccountingComponentType>(component.Type, "component.type"))
            .ToHashSet();
        if (components.Count != payload.Components.Length)
        {
            throw new ArgumentException("A mapping component may be declared only once.", nameof(json));
        }

        Dictionary<AccountingComponentType, (int Debits, int Credits)> directions = new();
        foreach (MappingLinePayload line in payload.Lines)
        {
            AccountingComponentType component = ParseEnum<AccountingComponentType>(line.Component, "line.component");
            if (!components.Contains(component))
            {
                throw new ArgumentException("A line references an undeclared component.", nameof(json));
            }

            ParseEnum<LedgerAccountRole>(line.Role, "line.role");
            DebitCreditIndicator direction = ParseEnum<DebitCreditIndicator>(line.Direction, "line.direction");
            (int debits, int credits) = directions.GetValueOrDefault(component);
            directions[component] = direction == DebitCreditIndicator.Debit
                ? (debits + 1, credits)
                : (debits, credits + 1);
        }

        if (components.Any(component => !directions.TryGetValue(component, out var count)
            || count.Debits == 0
            || count.Credits == 0
            || count.Debits != count.Credits))
        {
            throw new ArgumentException("Every component template must balance debit and credit lines exactly.", nameof(json));
        }

        return ConfigurationContent.Create(json);
    }

    private static T Deserialize<T>(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions)
                ?? throw new ArgumentException("Configuration content is required.", nameof(json));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Configuration content does not match its schema.", nameof(json), exception);
        }
    }

    private static T ParseEnum<T>(string value, string field) where T : struct, Enum
    {
        Required(value, field);
        if (!Enum.TryParse(value, ignoreCase: true, out T parsed) || !Enum.IsDefined(parsed))
        {
            throw new ArgumentException($"Unsupported {field} value.", field);
        }

        return parsed;
    }

    private static void Required(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{field} is required.", field);
        }
    }

    private sealed record ChartPayload(string LegalEntityCode, ChartAccountPayload[] Accounts);

    private sealed record ChartAccountPayload(string Code, string Role, string NormalSide, string Currency, string Status);

    private sealed record MappingPayload(
        string EventCode,
        string LegalEntityCode,
        MappingComponentPayload[] Components,
        MappingLinePayload[] Lines);

    private sealed record MappingComponentPayload(string Type, bool Required);

    private sealed record MappingLinePayload(string Component, string Role, string Direction);
}

public sealed record LedgerAccountClassification(string LegalEntityCode, string GlAccountCode);

