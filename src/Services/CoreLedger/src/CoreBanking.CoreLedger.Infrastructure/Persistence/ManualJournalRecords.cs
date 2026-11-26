using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

/// <summary>
/// One manual-journal request. The content hash, policy version and approval requirement are fixed at draft time and
/// never rewritten; only the lifecycle fields move. Decision: P1-E05-D11.
/// </summary>
public sealed class ManualJournalRequestRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string ManualJournalId { get; set; }
    public required string RequestId { get; set; }
    public required string Status { get; set; }
    public required string LegalEntityCode { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal TotalAmount { get; set; }
    public required string ReasonCode { get; set; }
    public required string EvidenceReference { get; set; }
    public required string RequestHash { get; set; }
    public required string PolicyVersionId { get; set; }
    public required string PolicyContentHash { get; set; }
    public int RequiredApprovers { get; set; }
    public bool RequiresFinanceHead { get; set; }
    public required string FinanceHeadPermission { get; set; }
    public required string MakerActorId { get; set; }
    public string? ApprovedByActorId { get; set; }
    public string? WorkflowRequestId { get; set; }
    public string? FinancialTransactionId { get; set; }
    public string? RefusalCode { get; set; }
    public bool BreakGlass { get; set; }
    public string? BreakGlassActorId { get; set; }
    public string? DualControlActorId { get; set; }
    public string? SecurityEvidenceReference { get; set; }
    public DateTimeOffset? BreakGlassValidUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public long Version { get; set; }
}

/// <summary>The immutable resolved lines of one submitted manual journal. Decision: P1-E05-D11.</summary>
public sealed class ManualJournalLineRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string ManualJournalId { get; set; }
    public int Sequence { get; set; }
    public required string LedgerAccountId { get; set; }
    public required string ChartAccountCode { get; set; }
    public required string AccountRole { get; set; }
    public required string AccountClass { get; set; }
    public required string Direction { get; set; }
    public decimal Amount { get; set; }
    public required string Component { get; set; }
    public bool RequiresCompensatingAccount { get; set; }
}

/// <summary>One append-only Workflow approval of the exact submitted manual journal. Decision: P1-E05-D11.</summary>
public sealed class ManualJournalApprovalRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string ManualJournalId { get; set; }
    public required string DecisionId { get; set; }
    public required string ApproverActorId { get; set; }
    public required string Permission { get; set; }
    public bool IsFinanceHead { get; set; }
    public string? EntitlementVersion { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
}
