using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Ledger;

namespace CoreBanking.CoreLedger.Application.Ledger;

public sealed record ManualJournalLineCommand(
    string LedgerAccountId,
    string Direction,
    decimal Amount,
    string Component);

public sealed record CreateManualJournalDraftCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string ManualJournalId,
    string RequestId,
    string ReasonCode,
    string EvidenceReference,
    IReadOnlyList<ManualJournalLineCommand> Lines,
    string CorrelationId);

public sealed record SubmitManualJournalCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string ManualJournalId,
    long ExpectedVersion,
    string RequestId,
    string SourceRequestKey,
    string CorrelationId);

public sealed record BreakGlassManualJournalCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string ManualJournalId,
    string RequestId,
    string ReasonCode,
    string EvidenceReference,
    IReadOnlyList<ManualJournalLineCommand> Lines,
    string DualControlActorId,
    string SecurityEvidenceReference,
    DateTimeOffset ValidUntil,
    string CorrelationId);

public sealed record ManualJournalResult(
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
    IReadOnlyList<string> AccountClasses,
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

/// <summary>
/// Owns the manual-journal lifecycle: an exact-hash draft, a submitted Workflow approval bound to that hash, and a
/// post only when the approved quorum is satisfied. Break-glass posts without the approval chain but records its own
/// security evidence. Decision: P1-E05-D11.
/// </summary>
public interface IManualJournalService
{
    Task<ManualJournalResult> CreateDraftAsync(
        CreateManualJournalDraftCommand command,
        CancellationToken cancellationToken);

    Task<ManualJournalResult> SubmitAsync(
        SubmitManualJournalCommand command,
        CancellationToken cancellationToken);

    Task<ManualJournalResult> PostWithBreakGlassAsync(
        BreakGlassManualJournalCommand command,
        CancellationToken cancellationToken);

    Task<ManualJournalResult> GetAsync(
        InstitutionId institutionId,
        string manualJournalId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Applies a Workflow decision to the exact submitted manual journal. It runs inside the decision consumer's unit
/// of work so the approval evidence, the final posting and the inbox dedup record commit together.
/// Decision: P1-E05-D11.
/// </summary>
public interface IManualJournalApprovalApplier
{
    Task<string> ApplyDecisionAsync(
        InstitutionId institutionId,
        string manualJournalId,
        string requestId,
        string subjectVersion,
        string subjectHash,
        bool approved,
        string approverActorId,
        string permission,
        string entitlementVersion,
        DateTimeOffset decidedAt,
        string decisionId,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed record CreateManualJournalPolicyDraftCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    string PolicyId,
    int PolicyVersion,
    string FinanceHeadPermission,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ManualJournalClassGrantCommand> Classes,
    IReadOnlyList<ManualJournalTierCommand> Tiers,
    string CorrelationId);

public sealed record ManualJournalClassGrantCommand(
    string Code,
    IReadOnlyList<string> Roles,
    bool RequiresCompensatingAccount,
    int MinimumApprovers);

public sealed record ManualJournalTierCommand(decimal? UpToAmount, int RequiredApprovers, bool RequiresFinanceHead);

public sealed record SimulateManualJournalPolicyCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    string SimulationId,
    string ContentHash,
    string CorrelationId);

public sealed record SubmitManualJournalPolicyCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string RequestId,
    string SourceRequestKey,
    string VersionId,
    string ContentHash,
    string CorrelationId);

public sealed record PublishManualJournalPolicyCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string PublicationId,
    string VersionId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string CorrelationId);

public interface IManualJournalPolicyConfigurationService
{
    Task<ConfigurationDraftOutcome> CreateDraftAsync(
        CreateManualJournalPolicyDraftCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationSimulationOutcome> SimulateAsync(
        SimulateManualJournalPolicyCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationSubmissionOutcome> SubmitForApprovalAsync(
        SubmitManualJournalPolicyCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationPublicationOutcome> PublishAsync(
        PublishManualJournalPolicyCommand command,
        CancellationToken cancellationToken);
}

public sealed record ManualJournalPolicyLookup(
    bool Found,
    string? RefusalCode,
    string? VersionId,
    string? ContentHash,
    ManualJournalPolicy? Policy)
{
    public static ManualJournalPolicyLookup FoundPolicy(
        string versionId,
        string contentHash,
        ManualJournalPolicy policy) =>
        new(true, null, versionId, contentHash, policy);

    public static ManualJournalPolicyLookup Rejected(string refusalCode) =>
        new(false, refusalCode, null, null, null);
}

/// <summary>
/// Resolves the effective, published manual-journal policy. Missing, overlapping, superseded or corrupt content
/// fails closed, so a manual journal is refused instead of defaulting a reason code, class or threshold.
/// Decision: P1-E05-D11.
/// </summary>
public interface IManualJournalPolicySource
{
    Task<ManualJournalPolicyLookup> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}

/// <summary>Resolves one ledger account's approved chart role without inventing a class. Decision: P1-E05-D11.</summary>
public sealed record ManualJournalAccountClassification(
    string LedgerAccountId,
    string CustomerAccountId,
    string ChartAccountCode,
    LedgerAccountRole Role,
    string LegalEntityCode,
    string Currency);

public sealed record ManualJournalAccountLookup(
    bool Found,
    string? RefusalCode,
    IReadOnlyList<ManualJournalAccountClassification>? Accounts)
{
    public static ManualJournalAccountLookup Resolved(IReadOnlyList<ManualJournalAccountClassification> accounts) =>
        new(true, null, accounts);

    public static ManualJournalAccountLookup Rejected(string refusalCode) => new(false, refusalCode, null);
}

public interface IManualJournalAccountResolver
{
    Task<ManualJournalAccountLookup> ResolveAsync(
        InstitutionId institutionId,
        IReadOnlyList<string> ledgerAccountIds,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}
