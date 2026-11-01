using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CustomerOnboarding.Contracts;
using CoreBanking.CustomerOnboarding.Infrastructure.Approvals;
using CoreBanking.CustomerOnboarding.Infrastructure.Institutions;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Messaging;

/// <summary>Routes each subscribed event family to its independently idempotent consumer.</summary>
public sealed class CustomerOnboardingEventRouter : IIntegrationEventConsumer
{
    private readonly InstitutionProvisioningConsumer _institutionProvisioning;
    private readonly CustomerClassificationApprovalDecidedConsumer _classificationApproval;
    private readonly CustomerMergeApprovalDecidedConsumer _mergeApproval;

    public CustomerOnboardingEventRouter(
        InstitutionProvisioningConsumer institutionProvisioning,
        CustomerClassificationApprovalDecidedConsumer classificationApproval,
        CustomerMergeApprovalDecidedConsumer mergeApproval)
    {
        _institutionProvisioning = institutionProvisioning ?? throw new ArgumentNullException(nameof(institutionProvisioning));
        _classificationApproval = classificationApproval ?? throw new ArgumentNullException(nameof(classificationApproval));
        _mergeApproval = mergeApproval ?? throw new ArgumentNullException(nameof(mergeApproval));
    }

    public string Topic => "multiple";

    private static string? SubjectTypeOf(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("SubjectType", out JsonElement subjectType)
                ? subjectType.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task<InboxResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!string.Equals(envelope.EventType, CustomerClassificationApprovalV1.DecidedEventType, StringComparison.Ordinal))
        {
            return _institutionProvisioning.HandleAsync(envelope, cancellationToken);
        }

        // One approval topic carries several subject types, so the payload decides which consumer owns it.
        return SubjectTypeOf(envelope.Payload) switch
        {
            CustomerMergeApprovalV1.SubjectType => _mergeApproval.HandleAsync(envelope, cancellationToken),
            CustomerClassificationApprovalV1.SubjectType => _classificationApproval.HandleAsync(envelope, cancellationToken),
            _ => throw new InvalidOperationException("No Customer and Onboarding consumer accepts this approval subject type."),
        };
    }
}
