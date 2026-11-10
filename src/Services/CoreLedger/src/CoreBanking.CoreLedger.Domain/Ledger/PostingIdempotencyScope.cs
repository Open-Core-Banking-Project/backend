using System;

namespace CoreBanking.CoreLedger.Domain.Ledger;

/// <summary>The complete uniqueness scope for one financial command. Decision: P1-E05-D08.</summary>
public sealed record PostingIdempotencyScope
{
    private PostingIdempotencyScope(
        FinancialReference institutionId,
        FinancialReference operation,
        FinancialReference initiator,
        FinancialReference key)
    {
        InstitutionId = institutionId;
        Operation = operation;
        Initiator = initiator;
        Key = key;
    }

    public FinancialReference InstitutionId { get; }

    public FinancialReference Operation { get; }

    public FinancialReference Initiator { get; }

    public FinancialReference Key { get; }

    public static PostingIdempotencyScope Create(
        string institutionId,
        string operation,
        string initiator,
        string key) =>
        new(
            FinancialReference.Parse(institutionId),
            FinancialReference.Parse(operation),
            FinancialReference.Parse(initiator),
            FinancialReference.Parse(key));
}

