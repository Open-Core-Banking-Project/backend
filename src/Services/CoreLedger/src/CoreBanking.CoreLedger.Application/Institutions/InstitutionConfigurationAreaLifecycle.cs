using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Institutions;

namespace CoreBanking.CoreLedger.Application.Institutions;

/// <summary>
/// One effective-dated institution configuration area: its identity, its schema, the set identifier
/// prefix and the validator that turns stored JSON into canonical content. Areas share one lifecycle
/// engine so draft, simulation evidence, Workflow approval and publication cannot drift apart.
/// </summary>
public sealed record InstitutionConfigurationArea(
    string Area,
    string Schema,
    string Scope,
    string SetIdPrefix,
    string CodePrefix,
    Func<string, ConfigurationContent> Validate);

public sealed record ConfigurationDraftOutcome(
    string InstitutionId,
    string VersionId,
    string ContentHash,
    string Status,
    DateTimeOffset ChangedAt);

public sealed record ConfigurationSimulationOutcome(
    string InstitutionId,
    string VersionId,
    string ContentHash,
    string InputHash,
    string OutputHash,
    string Outcome,
    DateTimeOffset SimulatedAt);

public sealed record ConfigurationSubmissionOutcome(
    string InstitutionId,
    string RequestId,
    string VersionId,
    string ContentHash,
    string EventId,
    string Status,
    DateTimeOffset SubmittedAt);

public sealed record ConfigurationPublicationOutcome(
    string InstitutionId,
    string PublicationId,
    string VersionId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset PublishedAt);

/// <summary>
/// The approved institution configuration lifecycle: draft, exact-content simulation evidence,
/// Workflow approval and effective-dated publication with no overlap and no self-publication.
/// Decisions: P1-E04-D07, P1-E04-D17.
/// </summary>
public interface IInstitutionConfigurationAreaLifecycle
{
    Task<ConfigurationDraftOutcome> CreateDraftAsync(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        ActorId actorId,
        string idempotencyKey,
        string versionId,
        ConfigurationContent content,
        string draftAuditAction,
        string correlationId,
        CancellationToken cancellationToken);

    Task<ConfigurationSimulationOutcome> SimulateAsync(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        ActorId actorId,
        string idempotencyKey,
        string versionId,
        string simulationId,
        string contentHash,
        string correlationId,
        CancellationToken cancellationToken);

    Task<ConfigurationSubmissionOutcome> SubmitAsync(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        ActorId actorId,
        string idempotencyKey,
        string requestId,
        string sourceRequestKey,
        string versionId,
        string contentHash,
        string correlationId,
        CancellationToken cancellationToken);

    Task<ConfigurationPublicationOutcome> PublishAsync(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        ActorId actorId,
        string idempotencyKey,
        string publicationId,
        string versionId,
        string contentHash,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        string correlationId,
        CancellationToken cancellationToken,
        Func<string, DateTimeOffset, CancellationToken, Task>? validateOnPublish = null);
}

public sealed class InstitutionConfigurationRejectedException : Exception
{
    public InstitutionConfigurationRejectedException(string code, string message) : base(message) => Code = code;

    public InstitutionConfigurationRejectedException(string code, string message, Exception inner)
        : base(message, inner) => Code = code;

    public string Code { get; }
}
