using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Institutions;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Authors <c>institution.account-numbering</c> through the shared configuration lifecycle. The stored
/// content is the canonical form of the validated scheme and no default scheme is ever created.
/// Decision: P1-E04-D06.
/// </summary>
public sealed class AccountNumberingConfigurationService : IAccountNumberingConfigurationService
{
    private static readonly InstitutionConfigurationArea Definition = new(
        AccountNumberScheme.Area,
        AccountNumberScheme.Schema,
        "institution",
        "institution-account-numbering",
        "account_numbering",
        contentJson => AccountNumberScheme.Parse(contentJson).ToConfigurationContent());

    private readonly IInstitutionConfigurationAreaLifecycle _lifecycle;

    public AccountNumberingConfigurationService(IInstitutionConfigurationAreaLifecycle lifecycle) =>
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));

    public Task<ConfigurationDraftOutcome> CreateDraftAsync(
        CreateAccountNumberingDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AccountNumberScheme scheme;
        try
        {
            scheme = AccountNumberScheme.Create(
                command.SchemeId,
                command.SchemeVersion,
                command.Prefix,
                command.ProductCodeWidth,
                command.BranchCodeWidth,
                command.SequenceWidth,
                AccountNumberScheme.ParseCheckDigit(command.CheckDigit));
        }
        catch (ArgumentException exception)
        {
            throw new InstitutionConfigurationRejectedException("account_numbering_invalid", exception.Message, exception);
        }

        return _lifecycle.CreateDraftAsync(
            Definition,
            command.InstitutionId,
            command.ActorId,
            command.IdempotencyKey,
            command.VersionId,
            scheme.ToConfigurationContent(),
            "institution.account_numbering.draft.create",
            command.CorrelationId,
            cancellationToken);
    }

    public Task<ConfigurationSimulationOutcome> SimulateAsync(
        SimulateAccountNumberingCommand command,
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
        SubmitAccountNumberingCommand command,
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
        PublishAccountNumberingCommand command,
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
