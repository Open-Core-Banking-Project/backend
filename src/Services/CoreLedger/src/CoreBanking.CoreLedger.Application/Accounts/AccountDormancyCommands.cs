using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Institutions;

namespace CoreBanking.CoreLedger.Application.Accounts;

public sealed record CreateDormancyRuleDraftCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    string RuleId,
    int RuleVersion,
    int ThresholdDays,
    int NoticeLeadDays,
    bool ReactivationRequiresApproval,
    IReadOnlyCollection<DormancyQualifyingActivity> QualifyingActivities,
    string CorrelationId);

public sealed record SimulateDormancyRuleCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    string SimulationId,
    string ContentHash,
    string CorrelationId);

public sealed record SubmitDormancyRuleCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string RequestId,
    string SourceRequestKey,
    string VersionId,
    string ContentHash,
    string CorrelationId);

public sealed record PublishDormancyRuleCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string PublicationId,
    string VersionId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string CorrelationId);

/// <summary>
/// Authors the <c>dormancy.rules</c> area through the approved lifecycle. No default threshold is ever
/// created. Decision: P1-E04-D12.
/// </summary>
public interface IAccountDormancyConfigurationService
{
    Task<ConfigurationDraftOutcome> CreateDraftAsync(
        CreateDormancyRuleDraftCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationSimulationOutcome> SimulateAsync(
        SimulateDormancyRuleCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationSubmissionOutcome> SubmitForApprovalAsync(
        SubmitDormancyRuleCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationPublicationOutcome> PublishAsync(
        PublishDormancyRuleCommand command,
        CancellationToken cancellationToken);
}

public sealed record DormancyRuleLookup(DormancyRule? Rule, string? VersionId, string? ContentHash, string? RejectionCode)
{
    public static DormancyRuleLookup Found(DormancyRule rule, string versionId, string contentHash) =>
        new(rule, versionId, contentHash, null);

    public static DormancyRuleLookup Rejected(string code) => new(null, null, null, code);
}

/// <summary>Resolves the effective, published <c>dormancy.rules</c> version. Decision: P1-E04-D12.</summary>
public interface IDormancyRuleSource
{
    Task<DormancyRuleLookup> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}

/// <summary>
/// The institution's current business date. Dormancy is never decided from wall-clock time, so this is the
/// only clock an assessment may use. Decision: P1-E04-D12.
/// </summary>
public interface IBusinessDateSource
{
    Task<DateOnly> GetCurrentBusinessDateAsync(
        InstitutionId institutionId,
        CancellationToken cancellationToken);
}

public sealed record AccountActivitySnapshot(
    IReadOnlyList<AccountMovementDescriptor> Movements,
    string SourceVersion,
    DateTimeOffset AsOf,
    string? UnavailableCode)
{
    public bool Available => UnavailableCode is null;
}

/// <summary>
/// The ledger's posted movement history by business date. It is the only qualifying-activity source; a summary,
/// a cache or a channel log is never accepted. Decision: P1-E04-D12.
/// </summary>
public interface IAccountActivitySource
{
    Task<AccountActivitySnapshot> GetAsync(
        InstitutionId institutionId,
        string accountId,
        string currencyCode,
        CancellationToken cancellationToken);
}

public sealed record AssessAccountDormancyCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    DateOnly? BusinessDate,
    string CorrelationId);

public sealed record DormancyAssessmentResult(
    string AccountId,
    string AssessmentId,
    DateOnly BusinessDate,
    string Decision,
    bool TransitionPermitted,
    bool NoticeDue,
    IReadOnlyList<string> ReasonCodes,
    DateOnly? LastQualifyingActivityDate,
    int InactivityDays,
    int ThresholdDays,
    string RuleVersionId,
    string Status,
    long AccountVersion,
    bool Replayed);

public sealed record ReactivateAccountCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    long ExpectedVersion,
    string ReasonCode,
    string? ApprovalReference,
    string? MakerActorId,
    string CorrelationId);

public sealed record AccountReactivatedResult(
    string AccountId,
    string MaskedAccountNumber,
    string Status,
    string ReasonCode,
    long AccountVersion,
    DateTimeOffset ReactivatedAt,
    bool Replayed);

/// <summary>
/// Runs the approved dormancy assessment and reactivation. An assessment records its outcome, including
/// "not dormant" and "not assessable", and only a permitted transition changes the account status.
/// Decision: P1-E04-D12.
/// </summary>
public interface IAccountDormancyService
{
    Task<DormancyAssessmentResult> AssessAsync(
        AssessAccountDormancyCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DormancyAssessmentResult>> AssessCatchUpAsync(
        InstitutionId institutionId,
        ActorId actorId,
        string accountId,
        DateOnly? fromBusinessDate,
        string correlationId,
        CancellationToken cancellationToken);

    Task<AccountReactivatedResult> ReactivateAsync(
        ReactivateAccountCommand command,
        CancellationToken cancellationToken);
}
