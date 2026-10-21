using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Products;

namespace CoreBanking.CoreLedger.Application.Products;

public sealed record CreateSavingsEligibilityDraftCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string ProductId,
    string ProductCode,
    string VersionId,
    int VersionNumber,
    ProductEligibilityRules Rules,
    string CorrelationId);

public sealed record SimulateSavingsEligibilityCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    string ContentHash,
    string SimulationId,
    IReadOnlyCollection<ProductEligibilitySimulationInput> Scenarios,
    string CorrelationId);

public sealed record SubmitSavingsEligibilityForApprovalCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string RequestId,
    string SourceRequestKey,
    string VersionId,
    string ContentHash,
    string CorrelationId);

public sealed record PublishSavingsEligibilityCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string PublicationId,
    string VersionId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string CorrelationId);

public sealed record SavingsEligibilityDraftResult(
    string InstitutionId,
    string ProductId,
    string ProductCode,
    string VersionId,
    int VersionNumber,
    string ContentHash,
    string Status,
    DateTimeOffset ChangedAt,
    bool Replayed);

public sealed record SavingsEligibilitySimulationCommandResult(
    string InstitutionId,
    string VersionId,
    string SimulationId,
    string ContentHash,
    string InputHash,
    string OutputHash,
    string Outcome,
    DateTimeOffset SimulatedAt,
    bool Replayed);

public sealed record SavingsEligibilityApprovalSubmissionResult(
    string InstitutionId,
    string RequestId,
    string VersionId,
    string ContentHash,
    string EventId,
    string Status,
    DateTimeOffset SubmittedAt,
    bool Replayed);

public sealed record SavingsEligibilityPublicationResult(
    string InstitutionId,
    string PublicationId,
    string ProductId,
    string VersionId,
    string ContentHash,
    string EventId,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset PublishedAt,
    bool Replayed);

public interface ISavingsProductConfigurationService
{
    Task<SavingsEligibilityDraftResult> CreateDraftAsync(
        CreateSavingsEligibilityDraftCommand command,
        CancellationToken cancellationToken);

    Task<SavingsEligibilitySimulationCommandResult> SimulateAsync(
        SimulateSavingsEligibilityCommand command,
        CancellationToken cancellationToken);

    Task<SavingsEligibilityApprovalSubmissionResult> SubmitForApprovalAsync(
        SubmitSavingsEligibilityForApprovalCommand command,
        CancellationToken cancellationToken);

    Task<SavingsEligibilityPublicationResult> PublishAsync(
        PublishSavingsEligibilityCommand command,
        CancellationToken cancellationToken);
}

public sealed class SavingsProductConfigurationRejectedException : Exception
{
    public SavingsProductConfigurationRejectedException(string code, string message) : base(message) => Code = code;
    public SavingsProductConfigurationRejectedException(string code, string message, Exception inner) : base(message, inner) => Code = code;
    public string Code { get; }
}

public sealed class SavingsProductConfigurationInProgressException : Exception
{
    public SavingsProductConfigurationInProgressException(string message) : base(message) { }
}
