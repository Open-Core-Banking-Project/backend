using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Accounts;

namespace CoreBanking.CoreLedger.Application.Accounts;

public sealed record CreateAccountOpeningPolicyDraftCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    AccountLimitKey LimitKey,
    int MaximumOpenAccounts,
    int OverrideAdditionalAccounts,
    string CorrelationId);

public sealed record AccountOpeningPolicyDraftResult(
    string InstitutionId,
    string VersionId,
    string ContentHash,
    string Status,
    DateTimeOffset ChangedAt,
    bool Replayed);

public sealed record SimulateAccountOpeningPolicyCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    string SimulationId,
    string ContentHash,
    string CorrelationId);

public sealed record AccountOpeningPolicySimulationResult(
    string InstitutionId,
    string VersionId,
    string ContentHash,
    string InputHash,
    string OutputHash,
    string Outcome,
    DateTimeOffset SimulatedAt,
    bool Replayed);

public sealed record SubmitAccountOpeningPolicyCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string RequestId,
    string SourceRequestKey,
    string VersionId,
    string ContentHash,
    string CorrelationId);

public sealed record AccountOpeningPolicySubmissionResult(
    string InstitutionId,
    string RequestId,
    string VersionId,
    string ContentHash,
    string EventId,
    string Status,
    DateTimeOffset SubmittedAt,
    bool Replayed);

public sealed record PublishAccountOpeningPolicyCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string PublicationId,
    string VersionId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string CorrelationId);

public sealed record AccountOpeningPolicyPublicationResult(
    string InstitutionId,
    string PublicationId,
    string VersionId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset PublishedAt,
    bool Replayed);

/// <summary>
/// Authors the <c>institution.account-opening-policy</c> area through the approved lifecycle:
/// draft, exact-content simulation, Workflow approval and effective-dated publication.
/// Decision: P1-E04-D07.
/// </summary>
public interface IAccountOpeningPolicyConfigurationService
{
    Task<AccountOpeningPolicyDraftResult> CreateDraftAsync(
        CreateAccountOpeningPolicyDraftCommand command,
        CancellationToken cancellationToken);

    Task<AccountOpeningPolicySimulationResult> SimulateAsync(
        SimulateAccountOpeningPolicyCommand command,
        CancellationToken cancellationToken);

    Task<AccountOpeningPolicySubmissionResult> SubmitForApprovalAsync(
        SubmitAccountOpeningPolicyCommand command,
        CancellationToken cancellationToken);

    Task<AccountOpeningPolicyPublicationResult> PublishAsync(
        PublishAccountOpeningPolicyCommand command,
        CancellationToken cancellationToken);
}

public sealed class AccountOpeningPolicyRejectedException : Exception
{
    public AccountOpeningPolicyRejectedException(string code, string message) : base(message) => Code = code;

    public AccountOpeningPolicyRejectedException(string code, string message, Exception inner)
        : base(message, inner) => Code = code;

    public string Code { get; }
}
