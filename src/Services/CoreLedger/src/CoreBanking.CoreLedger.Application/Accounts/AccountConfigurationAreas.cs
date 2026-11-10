using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Institutions;
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

public sealed record SimulateAccountOpeningPolicyCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    string SimulationId,
    string ContentHash,
    string CorrelationId);

public sealed record SubmitAccountOpeningPolicyCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string RequestId,
    string SourceRequestKey,
    string VersionId,
    string ContentHash,
    string CorrelationId);

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

/// <summary>
/// Authors the <c>institution.account-opening-policy</c> area through the approved lifecycle.
/// Decision: P1-E04-D07.
/// </summary>
public interface IAccountOpeningPolicyConfigurationService
{
    Task<ConfigurationDraftOutcome> CreateDraftAsync(
        CreateAccountOpeningPolicyDraftCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationSimulationOutcome> SimulateAsync(
        SimulateAccountOpeningPolicyCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationSubmissionOutcome> SubmitForApprovalAsync(
        SubmitAccountOpeningPolicyCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationPublicationOutcome> PublishAsync(
        PublishAccountOpeningPolicyCommand command,
        CancellationToken cancellationToken);
}

public sealed record CreateAccountNumberingDraftCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    string SchemeId,
    int SchemeVersion,
    string Prefix,
    int ProductCodeWidth,
    int BranchCodeWidth,
    int SequenceWidth,
    string CheckDigit,
    string CorrelationId);

public sealed record SimulateAccountNumberingCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    string SimulationId,
    string ContentHash,
    string CorrelationId);

public sealed record SubmitAccountNumberingCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string RequestId,
    string SourceRequestKey,
    string VersionId,
    string ContentHash,
    string CorrelationId);

public sealed record PublishAccountNumberingCommand(
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
/// Authors the <c>institution.account-numbering</c> area through the approved lifecycle.
/// Decision: P1-E04-D06.
/// </summary>
public interface IAccountNumberingConfigurationService
{
    Task<ConfigurationDraftOutcome> CreateDraftAsync(
        CreateAccountNumberingDraftCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationSimulationOutcome> SimulateAsync(
        SimulateAccountNumberingCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationSubmissionOutcome> SubmitForApprovalAsync(
        SubmitAccountNumberingCommand command,
        CancellationToken cancellationToken);

    Task<ConfigurationPublicationOutcome> PublishAsync(
        PublishAccountNumberingCommand command,
        CancellationToken cancellationToken);
}
