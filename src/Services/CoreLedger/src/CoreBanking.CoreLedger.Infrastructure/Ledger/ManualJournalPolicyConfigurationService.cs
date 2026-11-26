using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Application.Ledger;
using CoreBanking.CoreLedger.Domain.Ledger;

namespace CoreBanking.CoreLedger.Infrastructure.Ledger;

/// <summary>
/// Authors <c>ledger.manual_journal_policy</c> through the shared configuration lifecycle. The stored content is the
/// canonical form of the validated policy and no default policy is ever created. Decision: P1-E05-D11.
/// </summary>
public sealed class ManualJournalPolicyConfigurationService : IManualJournalPolicyConfigurationService
{
    private static readonly InstitutionConfigurationArea Definition = new(
        ManualJournalPolicy.Area,
        ManualJournalPolicy.Schema,
        "institution",
        "manual-journal-policy",
        "manual_journal_policy",
        contentJson => ManualJournalPolicy.Parse(contentJson).ToConfigurationContent());

    private readonly IInstitutionConfigurationAreaLifecycle _lifecycle;

    public ManualJournalPolicyConfigurationService(IInstitutionConfigurationAreaLifecycle lifecycle) =>
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));

    public Task<ConfigurationDraftOutcome> CreateDraftAsync(
        CreateManualJournalPolicyDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ManualJournalPolicy policy;
        try
        {
            policy = ManualJournalPolicy.Create(
                command.PolicyId,
                command.PolicyVersion,
                command.FinanceHeadPermission,
                command.ReasonCodes,
                command.Classes.Select(item => new ManualJournalClassGrant(
                    item.Code,
                    item.Roles.Select(role => ManualJournalRoles.Parse(role)).ToArray(),
                    item.RequiresCompensatingAccount,
                    item.MinimumApprovers)),
                command.Tiers.Select(item =>
                    new ManualJournalTier(item.UpToAmount, item.RequiredApprovers, item.RequiresFinanceHead)));
        }
        catch (ArgumentException exception)
        {
            throw new InstitutionConfigurationRejectedException(
                "manual_journal_policy_invalid",
                exception.Message,
                exception);
        }

        return _lifecycle.CreateDraftAsync(
            Definition,
            command.InstitutionId,
            command.ActorId,
            command.IdempotencyKey,
            command.VersionId,
            policy.ToConfigurationContent(),
            "ledger.manual_journal_policy.draft.create",
            command.CorrelationId,
            cancellationToken);
    }

    public Task<ConfigurationSimulationOutcome> SimulateAsync(
        SimulateManualJournalPolicyCommand command,
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
        SubmitManualJournalPolicyCommand command,
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
        PublishManualJournalPolicyCommand command,
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

/// <summary>Maps the configuration's chart-role codes onto the ledger domain enum.</summary>
public static class ManualJournalRoles
{
    public static LedgerAccountRole Parse(string value) => (value ?? string.Empty).Trim() switch
    {
        "customerDeposit" => LedgerAccountRole.CustomerDeposit,
        "cashSettlement" => LedgerAccountRole.CashSettlement,
        "internalTransferClearing" => LedgerAccountRole.InternalTransferClearing,
        "feeIncome" => LedgerAccountRole.FeeIncome,
        "commissionPayable" => LedgerAccountRole.CommissionPayable,
        "taxPayable" => LedgerAccountRole.TaxPayable,
        "interestExpense" => LedgerAccountRole.InterestExpense,
        "penaltyIncome" => LedgerAccountRole.PenaltyIncome,
        "suspense" => LedgerAccountRole.Suspense,
        _ => throw new ArgumentException("Unsupported chart role.", nameof(value)),
    };
}
