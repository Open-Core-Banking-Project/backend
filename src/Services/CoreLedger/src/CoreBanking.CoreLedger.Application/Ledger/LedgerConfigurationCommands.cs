using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Institutions;

namespace CoreBanking.CoreLedger.Application.Ledger;

public enum LedgerConfigurationKind
{
    Chart,
    Mapping,
}

public sealed record CreateLedgerConfigurationDraftCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    LedgerConfigurationKind Kind,
    string VersionId,
    string ContentJson,
    string CorrelationId);

public sealed record SimulateLedgerConfigurationCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    LedgerConfigurationKind Kind,
    string VersionId,
    string SimulationId,
    string ContentHash,
    string CorrelationId);

public sealed record SubmitLedgerConfigurationCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    LedgerConfigurationKind Kind,
    string RequestId,
    string SourceRequestKey,
    string VersionId,
    string ContentHash,
    string CorrelationId);

public sealed record PublishLedgerConfigurationCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    LedgerConfigurationKind Kind,
    string PublicationId,
    string VersionId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string CorrelationId);

public interface ILedgerConfigurationService
{
    Task<ConfigurationDraftOutcome> CreateDraftAsync(CreateLedgerConfigurationDraftCommand command, CancellationToken cancellationToken);

    Task<ConfigurationSimulationOutcome> SimulateAsync(SimulateLedgerConfigurationCommand command, CancellationToken cancellationToken);

    Task<ConfigurationSubmissionOutcome> SubmitAsync(SubmitLedgerConfigurationCommand command, CancellationToken cancellationToken);

    Task<ConfigurationPublicationOutcome> PublishAsync(PublishLedgerConfigurationCommand command, CancellationToken cancellationToken);
}

