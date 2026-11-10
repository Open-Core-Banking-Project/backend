using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

/// <summary>
/// One recorded dormancy assessment. The natural key is (account, business date, rule version), so a repeated
/// run returns the recorded outcome instead of assessing the same day twice. Decision: P1-E04-D12.
/// </summary>
public sealed class AccountDormancyAssessmentRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string AssessmentId { get; set; }
    public required string AccountId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public required string RuleVersionId { get; set; }
    public required string RuleContentHash { get; set; }
    public int ThresholdDays { get; set; }
    public int NoticeLeadDays { get; set; }
    public DateOnly ClockOriginDate { get; set; }
    public DateOnly? LastQualifyingActivityDate { get; set; }
    public int InactivityDays { get; set; }
    public required string Decision { get; set; }
    public required string ReasonCodes { get; set; }
    public bool TransitionPermitted { get; set; }
    public string? PreviousStatus { get; set; }
    public string? ResultingStatus { get; set; }
    public DateTimeOffset AssessedAt { get; set; }
}

/// <summary>
/// A pre-dormancy notice fact. Where the rule requires notice, the transition is refused until one is recorded.
/// Decision: P1-E04-D12.
/// </summary>
public sealed class AccountDormancyNoticeRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string NoticeId { get; set; }
    public required string AccountId { get; set; }
    public required string RuleVersionId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public required string ChannelCode { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>
/// A short-lived per-account assessment lease, so two workers can never assess the same account at once. The
/// unique index over the released flag is the concurrency guard. Decision: P1-E04-D12.
/// </summary>
public sealed class AccountDormancyLeaseRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string LeaseId { get; set; }
    public required string AccountId { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
}

/// <summary>
/// One closure proposal together with the condition snapshot it was decided on: the ledger reference, version
/// and as-of, the balances the ledger reported and the refusing codes. The account module never forces a
/// balance to zero. Decision: P1-E04-D13.
/// </summary>
public sealed class AccountClosureProposalRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string ProposalId { get; set; }
    public required string AccountId { get; set; }
    public required string RequestId { get; set; }
    public required string ReasonCode { get; set; }
    public string? SupportingReference { get; set; }
    public required string Status { get; set; }
    public required string ConditionSnapshotHash { get; set; }
    public required string LedgerAccountReference { get; set; }
    public required string LedgerSnapshotVersion { get; set; }
    public DateTimeOffset ConditionsAsOf { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal LedgerBalance { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal Holds { get; set; }
    public decimal OutstandingLienAmount { get; set; }
    public int PendingSettlements { get; set; }
    public bool LienReleaseDecisionRecorded { get; set; }
    public bool DisputeOpen { get; set; }
    public bool NoticePending { get; set; }
    public required string RefusalCodes { get; set; }
    public string? ApprovalRequestId { get; set; }
    public string? ApprovedByActorId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public required string CreatedByActorId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
