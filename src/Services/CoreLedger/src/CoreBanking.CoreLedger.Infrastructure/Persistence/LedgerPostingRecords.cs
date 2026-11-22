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
