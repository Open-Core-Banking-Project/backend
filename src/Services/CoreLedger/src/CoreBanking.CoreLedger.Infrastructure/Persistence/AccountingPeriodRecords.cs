using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

/// <summary>
/// One legal-entity accounting period. The state is a financial control checked inside posting transactions; the
/// allowed-event list is the empty-by-default soft-close/reopen allow-list. Decision: P1-E05-D13.
/// </summary>
public sealed class AccountingPeriodRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string LegalEntityCode { get; set; }
    public required string PeriodCode { get; set; }
    public required string Status { get; set; }
    public required string AllowedEventCodesJson { get; set; }
    public long Version { get; set; }
    public decimal? ClosedDebitTotal { get; set; }
    public decimal? ClosedCreditTotal { get; set; }
    public string? CloseSummaryHash { get; set; }
    public string? CloseApprovalReference { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? ReopenExpiresAt { get; set; }
    public string? ReopenReasonCode { get; set; }
    public DateTimeOffset? ReclosedAt { get; set; }
    public string? RecloseReconciliationReference { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public string? LockApprovalReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Append-only evidence of every period transition. Decision: P1-E05-D13.</summary>
public sealed class AccountingPeriodHistoryRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string LegalEntityCode { get; set; }
    public required string PeriodCode { get; set; }
    public int Sequence { get; set; }
    public required string FromStatus { get; set; }
    public required string ToStatus { get; set; }
    public required string ActorId { get; set; }
    public string? ApprovalReference { get; set; }
    public string? AllowedEventCodesJson { get; set; }
    public string? ReasonCode { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// The durable recalculation/reconciliation handoff a historical value date creates. The ledger never rewrites an
/// earlier posted result; the follow-up work is recorded here. Decision: P1-E05-D12.
/// </summary>
public sealed class RecalculationHandoffRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string HandoffId { get; set; }
    public required string LegalEntityCode { get; set; }
    public required string PeriodCode { get; set; }
    public required string FinancialTransactionId { get; set; }
    public DateOnly ValueDate { get; set; }
    public int AgeDays { get; set; }
    public required string ReasonCode { get; set; }
    public required string EvidenceReference { get; set; }
    public required string ApprovalReference { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
