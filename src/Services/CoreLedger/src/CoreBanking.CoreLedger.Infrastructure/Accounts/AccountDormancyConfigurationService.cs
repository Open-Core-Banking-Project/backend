using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Institutions;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Authors <c>dormancy.rules</c> through the shared configuration lifecycle. The stored content is the canonical
/// form of the validated rule and no default threshold is ever created. Decision: P1-E04-D12.
/// </summary>
public sealed class AccountDormancyConfigurationService : IAccountDormancyConfigurationService
{
    private static readonly InstitutionConfigurationArea Definition = new(
        DormancyRule.Area,
        DormancyRule.Schema,
        "institution",
        "dormancy-rules",
        "dormancy_rules",
        contentJson => DormancyRule.Parse(contentJson).ToConfigurationContent());

    private readonly IInstitutionConfigurationAreaLifecycle _lifecycle;

    public AccountDormancyConfigurationService(IInstitutionConfigurationAreaLifecycle lifecycle) =>
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));

    public Task<ConfigurationDraftOutcome> CreateDraftAsync(
        CreateDormancyRuleDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        DormancyRule rule;
        try
        {
            rule = DormancyRule.Create(
                command.RuleId,
                command.RuleVersion,
                command.ThresholdDays,
                command.NoticeLeadDays,
                command.ReactivationRequiresApproval,
                command.QualifyingActivities);
        }
        catch (ArgumentException exception)
        {
            throw new InstitutionConfigurationRejectedException("dormancy_rule_invalid", exception.Message, exception);
        }

        return _lifecycle.CreateDraftAsync(
            Definition,
            command.InstitutionId,
            command.ActorId,
            command.IdempotencyKey,
            command.VersionId,
            rule.ToConfigurationContent(),
            "dormancy.rules.draft.create",
            command.CorrelationId,
            cancellationToken);
    }

    public Task<ConfigurationSimulationOutcome> SimulateAsync(
        SimulateDormancyRuleCommand command,
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
        SubmitDormancyRuleCommand command,
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
        PublishDormancyRuleCommand command,
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
