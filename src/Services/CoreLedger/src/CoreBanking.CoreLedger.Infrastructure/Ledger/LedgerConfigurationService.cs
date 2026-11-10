using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Application.Ledger;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Ledger;

namespace CoreBanking.CoreLedger.Infrastructure.Ledger;

public sealed class LedgerConfigurationService : ILedgerConfigurationService
{
    private static readonly InstitutionConfigurationArea Chart = new(
        LedgerConfigurationContent.ChartArea,
        LedgerConfigurationContent.ChartSchema,
        "institution",
        "ledger-chart",
        "ledger_chart",
        LedgerConfigurationContent.ValidateChart);

    private static readonly InstitutionConfigurationArea Mapping = new(
        LedgerConfigurationContent.MappingArea,
        LedgerConfigurationContent.MappingSchema,
        "institution",
        "ledger-mapping",
        "ledger_mapping",
        LedgerConfigurationContent.ValidateMapping);

    private readonly IInstitutionConfigurationAreaLifecycle _lifecycle;

    public LedgerConfigurationService(IInstitutionConfigurationAreaLifecycle lifecycle) =>
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));

    public Task<ConfigurationDraftOutcome> CreateDraftAsync(
        CreateLedgerConfigurationDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        InstitutionConfigurationArea area = Area(command.Kind);
        ConfigurationContent content;
        try
        {
            content = area.Validate(command.ContentJson);
        }
        catch (ArgumentException exception)
        {
            throw new InstitutionConfigurationRejectedException("ledger_configuration_invalid", exception.Message, exception);
        }

        return _lifecycle.CreateDraftAsync(
            area,
            command.InstitutionId,
            command.ActorId,
            command.IdempotencyKey,
            command.VersionId,
            content,
            string.Concat(area.Area, ".draft.create"),
            command.CorrelationId,
            cancellationToken);
    }

    public Task<ConfigurationSimulationOutcome> SimulateAsync(
        SimulateLedgerConfigurationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _lifecycle.SimulateAsync(
            Area(command.Kind), command.InstitutionId, command.ActorId, command.IdempotencyKey,
            command.VersionId, command.SimulationId, command.ContentHash, command.CorrelationId, cancellationToken);
    }

    public Task<ConfigurationSubmissionOutcome> SubmitAsync(
        SubmitLedgerConfigurationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _lifecycle.SubmitAsync(
            Area(command.Kind), command.InstitutionId, command.ActorId, command.IdempotencyKey,
            command.RequestId, command.SourceRequestKey, command.VersionId, command.ContentHash,
            command.CorrelationId, cancellationToken);
    }

    public Task<ConfigurationPublicationOutcome> PublishAsync(
        PublishLedgerConfigurationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _lifecycle.PublishAsync(
            Area(command.Kind), command.InstitutionId, command.ActorId, command.IdempotencyKey,
            command.PublicationId, command.VersionId, command.ContentHash, command.EffectiveFrom,
            command.EffectiveTo, command.CorrelationId, cancellationToken);
    }

    private static InstitutionConfigurationArea Area(LedgerConfigurationKind kind) => kind switch
    {
        LedgerConfigurationKind.Chart => Chart,
        LedgerConfigurationKind.Mapping => Mapping,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

