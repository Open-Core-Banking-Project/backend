using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Infrastructure.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Products;

namespace CoreBanking.CoreLedger.Infrastructure;

/// <summary>
/// Routes one integration-event topic to the consumer that owns the subject. Approval decisions share a
/// topic, so the router reads the untrusted payload only to choose a consumer; each consumer validates the
/// envelope, the approved profile and the exact binding before anything changes.
/// </summary>
public sealed class CoreLedgerIntegrationEventConsumer : IIntegrationEventConsumer
{
    private readonly InstitutionBoundaryAcknowledgementConsumer _institutions;
    private readonly SavingsEligibilityApprovalDecidedConsumer _savingsApprovals;
    private readonly InstitutionConfigurationApprovalDecidedConsumer _configurationApprovals;
    private readonly AccountApplicationApprovalDecidedConsumer _accountApprovals;
    private readonly AccountClosureApprovalDecidedConsumer _closureApprovals;

    public CoreLedgerIntegrationEventConsumer(
        InstitutionBoundaryAcknowledgementConsumer institutions,
        SavingsEligibilityApprovalDecidedConsumer savingsApprovals,
        InstitutionConfigurationApprovalDecidedConsumer configurationApprovals,
        AccountApplicationApprovalDecidedConsumer accountApprovals,
        AccountClosureApprovalDecidedConsumer closureApprovals)
    {
        _institutions = institutions ?? throw new ArgumentNullException(nameof(institutions));
        _savingsApprovals = savingsApprovals ?? throw new ArgumentNullException(nameof(savingsApprovals));
        _configurationApprovals = configurationApprovals ?? throw new ArgumentNullException(nameof(configurationApprovals));
        _accountApprovals = accountApprovals ?? throw new ArgumentNullException(nameof(accountApprovals));
        _closureApprovals = closureApprovals ?? throw new ArgumentNullException(nameof(closureApprovals));
    }

    public string Topic => throw new NotSupportedException("The routing consumer subscribes to multiple configured topics.");

    public Task<InboxResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!string.Equals(envelope.EventType, SavingsEligibilityApprovalDecidedConsumer.EventType, StringComparison.Ordinal))
        {
            return _institutions.HandleAsync(envelope, cancellationToken);
        }

        return SubjectType(envelope) switch
        {
            AccountOpeningApprovalV1.SubjectType => _accountApprovals.HandleAsync(envelope, cancellationToken),
            AccountClosureApprovalV1.SubjectType => _closureApprovals.HandleAsync(envelope, cancellationToken),
            InstitutionConfigurationApprovalV1.SubjectType => _configurationApprovals.HandleAsync(envelope, cancellationToken),
            SavingsEligibilityApprovalV1.SubjectType => _savingsApprovals.HandleAsync(envelope, cancellationToken),
            _ => _institutions.HandleAsync(envelope, cancellationToken),
        };
    }

    private static string? SubjectType(IntegrationEventEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.Payload))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(envelope.Payload);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("subjectType", out JsonElement subject)
                && subject.ValueKind == JsonValueKind.String
                ? subject.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
