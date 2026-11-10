using System;

namespace CoreBanking.CoreLedger.Domain.Ledger;

public enum ExternalReferenceType
{
    ProviderReference,
    ChannelReference,
    SettlementReference,
    StatementReference,
    ExternalReference,
    InternalReference,
}

public sealed record FinancialReference
{
    private FinancialReference(string value) => Value = value;

    public string Value { get; }

    public static FinancialReference Parse(string value) => new(ReferenceGrammar.Validate(value, nameof(value)));

    public override string ToString() => Value;
}

public sealed record ExternalReference
{
    private ExternalReference(string producer, ExternalReferenceType type, FinancialReference reference)
    {
        Producer = producer;
        Type = type;
        Reference = reference;
    }

    public string Producer { get; }

    public ExternalReferenceType Type { get; }

    public FinancialReference Reference { get; }

    public static ExternalReference Create(string producer, ExternalReferenceType type, string reference) =>
        new(ReferenceGrammar.Validate(producer, nameof(producer)), type, FinancialReference.Parse(reference));
}

internal static class ReferenceGrammar
{
    public static string Validate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string candidate = value.Trim();

        if (candidate.Length > 128)
        {
            throw new ArgumentException("Reference must contain at most 128 characters.", parameterName);
        }

        foreach (char character in candidate)
        {
            if (character is < (char)0x20 or > (char)0x7E)
            {
                throw new ArgumentException("Reference must contain printable ASCII characters only.", parameterName);
            }
        }

        return candidate;
    }
}

