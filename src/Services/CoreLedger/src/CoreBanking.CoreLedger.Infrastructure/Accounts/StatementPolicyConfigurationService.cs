using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Institutions;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Authors <c>statement.policy</c> through the shared configuration lifecycle. The stored content is the canonical
/// form of the validated policy and no default policy is ever created. Decision: P1-E04-D15.
/// </summary>
public sealed class StatementPolicyConfigurationService : IStatementPolicyConfigurationService
{
    private static readonly InstitutionConfigurationArea Definition = new(
        StatementPolicy.Area,
        StatementPolicy.Schema,
        "institution",
        "statement-policy",
        "statement_policy",
        contentJson => StatementPolicy.Parse(contentJson).ToConfigurationContent());

    private readonly IInstitutionConfigurationAreaLifecycle _lifecycle;

    public StatementPolicyConfigurationService(IInstitutionConfigurationAreaLifecycle lifecycle) =>
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));

    public Task<ConfigurationDraftOutcome> CreateDraftAsync(
        CreateStatementPolicyDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        StatementPolicy policy;
        try
        {
            policy = StatementPolicy.Create(
                command.PolicyId,
                command.PolicyVersion,
                command.MaximumSpanDays,
                command.RetentionDays,
                command.ArtifactReferenceMinutes,
                command.Formats,
                command.Purposes,
                command.ReceiptFormats,
                command.PeriodKinds);
        }
        catch (ArgumentException exception)
        {
            throw new InstitutionConfigurationRejectedException("statement_policy_invalid", exception.Message, exception);
        }

        return _lifecycle.CreateDraftAsync(
            Definition,
            command.InstitutionId,
            command.ActorId,
            command.IdempotencyKey,
            command.VersionId,
            policy.ToConfigurationContent(),
            "statement.policy.draft.create",
            command.CorrelationId,
            cancellationToken);
    }

    public Task<ConfigurationSimulationOutcome> SimulateAsync(
        SimulateStatementPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _lifecycle.SimulateAsync(
            Definition,
            command.InstitutionId,
            command.ActorId,
            command.IdempotencyKey,
            command.VersionId,
            command.SimulationId,
            command.ContentHash,
            command.CorrelationId,
            cancellationToken);
    }

    public Task<ConfigurationSubmissionOutcome> SubmitForApprovalAsync(
        SubmitStatementPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _lifecycle.SubmitAsync(
            Definition,
            command.InstitutionId,
            command.ActorId,
            command.IdempotencyKey,
            command.RequestId,
            command.SourceRequestKey,
            command.VersionId,
            command.ContentHash,
            command.CorrelationId,
            cancellationToken);
    }

    public Task<ConfigurationPublicationOutcome> PublishAsync(
        PublishStatementPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _lifecycle.PublishAsync(
            Definition,
            command.InstitutionId,
            command.ActorId,
            command.IdempotencyKey,
            command.PublicationId,
            command.VersionId,
            command.ContentHash,
            command.EffectiveFrom,
            command.EffectiveTo,
            command.CorrelationId,
            cancellationToken);
    }
}
