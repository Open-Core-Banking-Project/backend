using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

public sealed class FinancialTransactionRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string FinancialTransactionId { get; set; }
    public required string TransactionReference { get; set; }
    public required string SourceService { get; set; }
    public required string SourceReference { get; set; }
    public string? ExternalReferenceProducer { get; set; }
    public string? ExternalReferenceType { get; set; }
    public string? ExternalReference { get; set; }
    public required string EventCode { get; set; }
    public required string RequestHash { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset TransactionTimestamp { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateOnly PostingDate { get; set; }
    public DateOnly ValueDate { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
}

public sealed class FinancialTransactionStatusHistoryRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string HistoryId { get; set; }
    public required string FinancialTransactionId { get; set; }
    public int Sequence { get; set; }
    public required string Status { get; set; }
    public required string ReasonCode { get; set; }
    public required string ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class JournalEntryRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string JournalEntryId { get; set; }
    public required string FinancialTransactionId { get; set; }
    public required string JournalReference { get; set; }
    public required string MappingVersionId { get; set; }
    public required string LegalEntityCode { get; set; }
    public required string CurrencyCode { get; set; }
    public required string Status { get; set; }
    public decimal TotalDebits { get; set; }
    public decimal TotalCredits { get; set; }
    public DateTimeOffset TransactionTimestamp { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateOnly PostingDate { get; set; }
    public DateOnly ValueDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class JournalLineRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string JournalLineId { get; set; }
    public required string JournalEntryId { get; set; }
    public int Sequence { get; set; }
    public required string LedgerAccountId { get; set; }
    public required string LegalEntityCode { get; set; }
    public required string CurrencyCode { get; set; }
    public required string Direction { get; set; }
    public required string ComponentType { get; set; }
    public decimal Amount { get; set; }
    public string? ReasonCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Append-only link and policy evidence for a reversal posting.</summary>
public sealed class FinancialReversalRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string FinancialReversalId { get; set; }
    public required string OriginalFinancialTransactionId { get; set; }
    public required string ReversalFinancialTransactionId { get; set; }
    public required string OriginalJournalEntryId { get; set; }
    public required string ReversalJournalEntryId { get; set; }
    public required string MappingVersionId { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal ReversedAmount { get; set; }
    public decimal RemainingAfter { get; set; }
    public required string ReasonCode { get; set; }
    public required string EvidenceReference { get; set; }
    public string? WorkflowDecisionId { get; set; }
    public string? MakerActorId { get; set; }
    public string? ApproverActorId { get; set; }
    public DateOnly OriginalValueDate { get; set; }
    public DateOnly ReversalValueDate { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>One authoritative, row-lockable balance. Totals are accumulated, never overwritten from a caller.</summary>
public sealed class LedgerBalanceRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string LedgerAccountId { get; set; }
    public required string CurrencyCode { get; set; }
    public required string NormalSide { get; set; }
    public decimal DebitTotal { get; set; }
    public decimal CreditTotal { get; set; }
    public decimal NetBalance { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal HoldRemaining { get; set; }
    public decimal PendingAuthorised { get; set; }
    public long Version { get; set; }
    public DateTimeOffset AsOf { get; set; }
}

/// <summary>Immutable effect of one journal line on one authoritative balance.</summary>
public sealed class LedgerMovementRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string MovementId { get; set; }
    public required string JournalLineId { get; set; }
    public required string LedgerAccountId { get; set; }
    public required string Direction { get; set; }
    public decimal Amount { get; set; }
    public decimal SignedAmount { get; set; }
    public decimal VisibleBalanceBefore { get; set; }
    public decimal VisibleBalanceAfter { get; set; }
    public decimal DebitTotalAfter { get; set; }
    public decimal CreditTotalAfter { get; set; }
    public long BalanceVersion { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Authoritative reservation that reduces available funds without changing ledger balance.</summary>
public sealed class LedgerHoldRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string HoldId { get; set; }
    public required string LedgerAccountId { get; set; }
    public required string SourceService { get; set; }
    public required string SourceReference { get; set; }
    public required string PurposeCode { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal ReleasedAmount { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Append-only evidence for every hold lifecycle transition.</summary>
public sealed class LedgerHoldHistoryRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string HistoryId { get; set; }
    public required string HoldId { get; set; }
    public int Sequence { get; set; }
    public required string Transition { get; set; }
    public decimal Amount { get; set; }
    public decimal RemainingAfter { get; set; }
    public string? FinancialTransactionId { get; set; }
    public required string ReasonCode { get; set; }
    public required string ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
