using System;

namespace CoreBanking.CoreLedger.Contracts;

/// <summary>The four financial clocks are deliberately separate in every posting contract.</summary>
public sealed record FinancialDatesV1(
    DateTimeOffset TransactionTimestamp,
    DateOnly BusinessDate,
    DateOnly PostingDate,
    DateOnly ValueDate);

/// <summary>Caller-owned references. Ledger identities and journal lines are never client assignable here.</summary>
public sealed record FinancialReferencesV1(
    string SourceService,
    string SourceReference,
    string? ExternalProducer,
    string? ExternalReferenceType,
    string? ExternalReference);

/// <summary>Explicit retry scope for a financial command.</summary>
public sealed record FinancialIdempotencyV1(
    string Operation,
    string Key);

