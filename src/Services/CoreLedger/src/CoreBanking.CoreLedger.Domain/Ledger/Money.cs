using System;
using System.Globalization;

namespace CoreBanking.CoreLedger.Domain.Ledger;

/// <summary>Exact monetary value for P1-E05. Creation validates the approved numeric(19,4) boundary.</summary>
public sealed record Money
{
    private const decimal MaximumMagnitude = 999_999_999_999_999.9999m;

    private Money(decimal amount, CurrencyCode currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public CurrencyCode Currency { get; }

    public static Money Create(decimal amount, string currency)
    {
        if (Math.Abs(amount) > MaximumMagnitude)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount exceeds numeric(19,4).");
        }

        int scale = (decimal.GetBits(amount)[3] >> 16) & 0x7F;
        if (scale > 4)
        {
            throw new ArgumentException("Amount must have no more than four decimal places.", nameof(amount));
        }

        return new Money(amount, CurrencyCode.Parse(currency));
    }

    public override string ToString() => Amount.ToString("0.####", CultureInfo.InvariantCulture) + " " + Currency.Value;
}

public sealed record CurrencyCode
{
    private CurrencyCode(string value) => Value = value;

    public string Value { get; }

    public static CurrencyCode Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string candidate = value.Trim();

        if (candidate.Length != 3
            || candidate[0] is < 'A' or > 'Z'
            || candidate[1] is < 'A' or > 'Z'
            || candidate[2] is < 'A' or > 'Z')
        {
            throw new ArgumentException("Currency must be a three-letter uppercase ISO code.", nameof(value));
        }

        return new CurrencyCode(candidate);
    }

    public override string ToString() => Value;
}

