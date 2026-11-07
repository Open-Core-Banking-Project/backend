using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Domain.Accounts;

namespace CoreBanking.CoreLedger.Application.Accounts;

/// <summary>
/// One account summary. It is a read model and never a financial source: the balances are the ledger's answer with
/// its source reference, or absent when the ledger cannot answer. Decisions: P1-E04-D11, P1-E04-D15.
/// </summary>
/// <summary>
/// Whether the summary carries the ledger's answer or nothing at all. It is never a third state such as "zero", which
/// would be a balance the module invented. Decision: P1-E04-D15.
/// </summary>
public static class AccountBalanceAvailability
{
    public const string Available = "ledger_available";
    public const string Unavailable = "ledger_unavailable";
}

public sealed record AccountSummaryResult(
    string InstitutionId,
    string AccountId,
    string MaskedAccountNumber,
    string CustomerId,
    string ProductId,
    string ProductVersionId,
    string BranchId,
    string CurrencyCode,
    string Status,
    long AccountVersion,
    string BalanceAvailability,
    string? LedgerAccountReference,
    LedgerAvailabilitySnapshot? Balances,
    decimal OutstandingLienAmount,
    int ActiveBlockCount,
    long ControlVersion,
    DateTimeOffset AsOf);

/// <summary>Reads one account summary for an authorised caller in the validated institution. Decision: P1-E04-D15.</summary>
public interface IAccountSummaryService
{
    Task<AccountSummaryResult> GetAsync(
        InstitutionId institutionId,
        string accountId,
        CancellationToken cancellationToken);
}

public sealed record CreateStatementPolicyDraftCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    string PolicyId,
    int PolicyVersion,
    int MaximumSpanDays,
    int RetentionDays,
    int ArtifactReferenceMinutes,
    IReadOnlyCollection<string> Formats,
    IReadOnlyCollection<string> Purposes,
    IReadOnlyCollection<string> ReceiptFormats,
    IReadOnlyCollection<string> PeriodKinds,
    string CorrelationId);

public sealed record SimulateStatementPolicyCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    string SimulationId,
    string ContentHash,
    string CorrelationId);

public sealed record SubmitStatementPolicyCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string RequestId,
    string SourceRequestKey,
    string VersionId,
    string ContentHash,
    string CorrelationId);

public sealed record PublishStatementPolicyCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string PublicationId,
    string VersionId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string CorrelationId);

/// <summary>Authors the <c>statement.policy</c> area through the approved lifecycle. Decision: P1-E04-D15.</summary>
public interface IStatementPolicyConfigurationService
{
    Task<ConfigurationDraftOutcome> CreateDraftAsync(
        CreateStatementPolicyDraftCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationSimulationOutcome> SimulateAsync(
        SimulateStatementPolicyCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationSubmissionOutcome> SubmitForApprovalAsync(
        SubmitStatementPolicyCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationPublicationOutcome> PublishAsync(
        PublishStatementPolicyCommand command,
        CancellationToken cancellationToken);
}

public sealed record StatementPolicyLookup(StatementPolicy? Policy, string? VersionId, string? ContentHash, string? RejectionCode)
{
    public static StatementPolicyLookup Found(StatementPolicy policy, string versionId, string contentHash) =>
        new(policy, versionId, contentHash, null);

    public static StatementPolicyLookup Rejected(string code) => new(null, null, null, code);
}

public interface IStatementPolicySource
{
    Task<StatementPolicyLookup> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}

/// <summary>
/// The ledger's authoritative statement query. It returns an opaque query reference, the integrity reference that
/// the artifact must carry and the opening and closing balances the ledger computed, or an explicit unavailability.
/// A statement is never built from a summary, a cache or a re-derived balance. Decision: P1-E04-D15.
/// </summary>
public sealed record LedgerStatementQueryResult(
    bool Available,
    string? UnavailableCode,
    string QueryReference,
    string IntegrityReference,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    int MovementCount);

public interface ILedgerStatementQuerySource
{
    Task<LedgerStatementQueryResult> GetAsync(
        InstitutionId institutionId,
        string accountId,
        string currencyCode,
        DateOnly periodFrom,
        DateOnly periodTo,
        CancellationToken cancellationToken);
}

public sealed record CreateAccountStatementRequestCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    string RequestId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string FormatCode,
    string PurposeCode,
    string RequesterTypeCode,
    string CorrelationId);

public sealed record AccountStatementRequestResult(
    string InstitutionId,
    string RequestId,
    string AccountId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string FormatCode,
    string PurposeCode,
    string Status,
    string LedgerQueryReference,
    string IntegrityReference,
    string? ArtifactReference,
    DateTimeOffset? ArtifactReadyAt,
    IReadOnlyList<string> RefusalCodes,
    string RequesterTypeCode,
    DateTimeOffset RequestedAt,
    bool Replayed);

public sealed record AuthoriseStatementArtifactCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    string RequestId,
    string RequesterTypeCode,
    string CorrelationId);

public sealed record StatementArtifactAuthorisationResult(
    string RequestId,
    string AccountId,
    string ArtifactReference,
    string IntegrityReference,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RetentionExpiresAt,
    string Status,
    bool Replayed);

/// <summary>
/// Authorises statement requests and hands them to the ledger and the document capability. The module validates the
/// period, the format and the purpose, records the request with no content, and refuses rather than substituting a
/// summary source. Decision: P1-E04-D15.
/// </summary>
public interface IAccountStatementService
{
    Task<AccountStatementRequestResult> RequestAsync(
        CreateAccountStatementRequestCommand command,
        CancellationToken cancellationToken);

    Task<AccountStatementRequestResult> GetAsync(
        InstitutionId institutionId,
        string accountId,
        string requestId,
        CancellationToken cancellationToken);

    Task<StatementArtifactAuthorisationResult> AuthoriseArtifactAsync(
        AuthoriseStatementArtifactCommand command,
        CancellationToken cancellationToken);
}
