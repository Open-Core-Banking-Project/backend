using System;

namespace CoreBanking.CoreLedger.Domain.Ledger;

/// <summary>Keeps the four financial clocks distinct. Decision: P1-E05-D12.</summary>
public sealed record FinancialDates
{
    private FinancialDates(
        DateTimeOffset transactionTimestamp,
        DateOnly businessDate,
        DateOnly postingDate,
        DateOnly valueDate)
    {
        TransactionTimestamp = transactionTimestamp;
        BusinessDate = businessDate;
        PostingDate = postingDate;
        ValueDate = valueDate;
    }

    public DateTimeOffset TransactionTimestamp { get; }

    public DateOnly BusinessDate { get; }

    public DateOnly PostingDate { get; }

    public DateOnly ValueDate { get; }

    public static FinancialDates Create(
        DateTimeOffset transactionTimestamp,
        DateOnly businessDate,
        DateOnly postingDate,
        DateOnly valueDate) =>
        new(transactionTimestamp, businessDate, postingDate, valueDate);
}

