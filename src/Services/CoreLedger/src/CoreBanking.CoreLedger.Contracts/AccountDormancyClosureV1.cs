using System;
using System.Collections.Generic;

namespace CoreBanking.CoreLedger.Contracts;

/// <summary>Request bodies for the institution dormancy-rule lifecycle. Decision: P1-E04-D12.</summary>
public sealed record CreateDormancyRuleDraftRequestV1(
    string VersionId,
    string RuleId,
    int RuleVersion,
    int ThresholdDays,
    int NoticeLeadDays,
    bool ReactivationRequiresApproval,
    IReadOnlyCollection<DormancyQualifyingActivityV1> QualifyingActivities);

public sealed record DormancyQualifyingActivityV1(string ActivityCode, string Direction);

public sealed record SimulateDormancyRuleRequestV1(string SimulationId, string ContentHash);

public sealed record SubmitDormancyRuleRequestV1(string RequestId, string SourceRequestKey, string ContentHash);

public sealed record PublishDormancyRuleRequestV1(
    string PublicationId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record DormancyAssessmentRequestV1(string? BusinessDate);

public sealed record DormancyAssessmentResponseV1(
    string InstitutionId,
    string AccountId,
    string AssessmentId,
    string BusinessDate,
    string Decision,
    bool TransitionPermitted,
    bool NoticeDue,
    IReadOnlyCollection<string> ReasonCodes,
    string? LastQualifyingActivityDate,
    int InactivityDays,
    int ThresholdDays,
    string RuleVersionId,
    string Status,
    long AccountVersion,
    bool Replayed);

public sealed record ReactivateAccountRequestV1(
    long ExpectedVersion,
    string ReasonCode,
    string? ApprovalReference,
    string? MakerActorId);

public sealed record AccountReactivatedResponseV1(
    string InstitutionId,
    string AccountId,
    string MaskedAccountNumber,
    string Status,
    string ReasonCode,
    long AccountVersion,
    DateTimeOffset ReactivatedAt,
    bool Replayed);

public sealed record CreateAccountClosureProposalRequestV1(
    string RequestId,
    string ReasonCode,
    string? SupportingReference,
    bool LienReleaseDecisionRecorded,
    bool DisputeOpen,
    bool NoticePending);

public sealed record SubmitAccountClosureProposalRequestV1(
    long ExpectedVersion,
    string RequestId,
    string SourceRequestKey);

public sealed record RecheckAccountClosureRequestV1(long ExpectedVersion);

public sealed record AccountClosureProposalResponseV1(
    string InstitutionId,
    string ProposalId,
    string AccountId,
    string RequestId,
    string Status,
    string ReasonCode,
    string? SupportingReference,
    IReadOnlyCollection<string> RefusalCodes,
    string ConditionSnapshotHash,
    string LedgerAccountReference,
    string LedgerSnapshotVersion,
    DateTimeOffset ConditionsAsOf,
    decimal LedgerBalance,
    decimal AvailableBalance,
    decimal Holds,
    decimal OutstandingLienAmount,
    int PendingSettlements,
    string? ApprovalRequestId,
    DateTimeOffset ChangedAt,
    long Version,
    bool Replayed);

public sealed record AccountClosedV1(
    string InstitutionId,
    string AccountId,
    string ProposalId,
    string LedgerAccountReference,
    string LedgerSnapshotVersion,
    long AccountVersion,
    DateTimeOffset ClosedAt);

/// <summary>
/// The approved approval profile for account closure. The approver decides the exact proposal version and the
/// condition snapshot hash the maker saw, and the maker can never approve it. Decision: P1-E04-D13.
/// </summary>
public static class AccountClosureApprovalV1
{
    public const string WorkflowKey = "account.close.v1";
    public const string WorkflowVersionId = "account-close-approval-v1";
    public const string SubjectType = "account.closure.proposal";
    public const string SourceService = "core-ledger";
    public const string RequiredPermission = "account.closure.approve";

    public static string WorkflowDefinitionHash { get; } = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(
            $"{WorkflowKey}|{WorkflowVersionId}|one-level|maker-excluded|{RequiredPermission}|decision-time-entitlement")))
        .ToLowerInvariant();
}
