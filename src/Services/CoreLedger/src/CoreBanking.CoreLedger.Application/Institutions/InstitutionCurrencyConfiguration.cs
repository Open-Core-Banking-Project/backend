using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Application.Institutions;

public sealed record CreateCurrencyDraftCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    string BaseCurrency,
    IReadOnlyList<string> SupportedCurrencies,
    string CorrelationId);

public sealed record CurrencyDraftResult(
    string InstitutionId,
    string VersionId,
    string ContentHash,
    string Status,
    DateTimeOffset ChangedAt,
    bool Replayed);

public sealed record SimulateCurrencyVersionCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    string SimulationId,
    string ContentHash,
    string CorrelationId);

public sealed record CurrencySimulationResult(
    string InstitutionId,
    string VersionId,
    string ContentHash,
    string InputHash,
    string OutputHash,
    string Outcome,
    DateTimeOffset SimulatedAt,
    bool Replayed);

public sealed record SubmitCurrencyVersionCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string RequestId,
    string SourceRequestKey,
    string VersionId,
    string ContentHash,
    string CorrelationId);

public sealed record CurrencySubmissionResult(
    string InstitutionId,
    string RequestId,
    string VersionId,
    string ContentHash,
    string EventId,
    string Status,
    DateTimeOffset SubmittedAt,
    bool Replayed);

public sealed record PublishCurrencyVersionCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string PublicationId,
    string VersionId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string CorrelationId);

public sealed record CurrencyPublicationResult(
    string InstitutionId,
    string PublicationId,
    string VersionId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset PublishedAt,
    bool Replayed);

/// <summary>
/// Authors the <c>institution.currencies</c> configuration area through the approved lifecycle:
/// draft, exact-content simulation, Workflow approval and effective-dated publication.
/// Decision: P1-E04-D17.
/// </summary>
public interface IInstitutionCurrencyConfigurationService
{
    Task<CurrencyDraftResult> CreateDraftAsync(CreateCurrencyDraftCommand command, CancellationToken cancellationToken);

    Task<CurrencySimulationResult> SimulateAsync(SimulateCurrencyVersionCommand command, CancellationToken cancellationToken);

    Task<CurrencySubmissionResult> SubmitForApprovalAsync(SubmitCurrencyVersionCommand command, CancellationToken cancellationToken);

    Task<CurrencyPublicationResult> PublishAsync(PublishCurrencyVersionCommand command, CancellationToken cancellationToken);
}

public sealed class InstitutionCurrencyConfigurationRejectedException : Exception
{
    public InstitutionCurrencyConfigurationRejectedException(string code, string message) : base(message) => Code = code;

    public InstitutionCurrencyConfigurationRejectedException(string code, string message, Exception inner) : base(message, inner) => Code = code;

    public string Code { get; }
}
