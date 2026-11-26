using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace CoreBanking.CoreLedger.Contracts;

/// <summary>Authoring bodies for the manual-journal policy configuration area. Decision: P1-E05-D11.</summary>
public sealed record CreateManualJournalPolicyDraftRequestV1(
    string VersionId,
    string PolicyId,
    int PolicyVersion,
    string FinanceHeadPermission,
    IReadOnlyCollection<string> ReasonCodes,
    IReadOnlyCollection<ManualJournalClassGrantRequestV1> Classes,
    IReadOnlyCollection<ManualJournalTierRequestV1> Tiers);

public sealed record ManualJournalClassGrantRequestV1(
    string Code,
    IReadOnlyCollection<string> Roles,
    bool RequiresCompensatingAccount,
    int MinimumApprovers);

public sealed record ManualJournalTierRequestV1(
    decimal? UpToAmount,
    int RequiredApprovers,
    bool RequiresFinanceHead);

public sealed record SimulateManualJournalPolicyRequestV1(string SimulationId, string ContentHash);

public sealed record SubmitManualJournalPolicyRequestV1(string RequestId, string SourceRequestKey, string ContentHash);

public sealed record PublishManualJournalPolicyRequestV1(
    string PublicationId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

/// <summary>Request bodies for the manual-journal lifecycle. Decision: P1-E05-D11.</summary>
public sealed record CreateManualJournalDraftRequestV1(
    string ManualJournalId,
    string RequestId,
    string ReasonCode,
    string EvidenceReference,
    IReadOnlyCollection<ManualJournalLineRequestV1> Lines);

public sealed record ManualJournalLineRequestV1(
    string LedgerAccountId,
    string Direction,
    decimal Amount,
    string Component);

public sealed record SubmitManualJournalRequestV1(
    long ExpectedVersion,
    string RequestId,
    string SourceRequestKey);

public sealed record BreakGlassManualJournalRequestV1(
    string ManualJournalId,
    string RequestId,
    string ReasonCode,
    string EvidenceReference,
    IReadOnlyCollection<ManualJournalLineRequestV1> Lines,
    string DualControlActorId,
    string SecurityEvidenceReference,
    DateTimeOffset ValidUntil);

public sealed record ManualJournalResponseV1(
    string InstitutionId,
    string ManualJournalId,
    string RequestId,
    string Status,
    string LegalEntityCode,
    string Currency,
    decimal TotalAmount,
    string ReasonCode,
    string EvidenceReference,
    string RequestHash,
    string PolicyVersionId,
    string PolicyContentHash,
    int RequiredApprovers,
    bool RequiresFinanceHead,
    IReadOnlyCollection<string> AccountClasses,
    string MakerActorId,
    int RecordedApprovals,
    string? ApprovedByActorId,
    string? WorkflowRequestId,
    string? FinancialTransactionId,
    string? RefusalCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? PostedAt,
    long Version,
    bool Replayed);

/// <summary>The published manual-journal posting fact.</summary>
public sealed record ManualJournalPostedV1(
    string ManualJournalId,
    string RequestId,
    string FinancialTransactionId,
    string TransactionReference,
    string JournalEntryId,
    string LegalEntityCode,
    string Currency,
    decimal TotalAmount,
    string ReasonCode,
    string EvidenceReference,
    string PolicyVersionId,
    int ApproverCount,
    bool BreakGlass,
    DateTimeOffset PostedAt);

/// <summary>
/// The approved approval profile for manual journals. The approver decides the exact submitted version and hash,
/// and the maker can never approve. Decision: P1-E05-D11.
/// </summary>
public static class ManualJournalApprovalV1
{
    public const string WorkflowKey = "ledger.manual_journal.v1";
    public const string WorkflowVersionId = "manual-journal-approval-v1";
    public const string SubjectType = "ledger.manual_journal.request";
    public const string SourceService = "core-ledger";
    public const string RequiredPermission = "ledger.manual_journal.approve";

    public static string WorkflowDefinitionHash { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{WorkflowKey}|{WorkflowVersionId}|tiered-levels|maker-excluded|{RequiredPermission}|decision-time-entitlement")))
        .ToLowerInvariant();
}
