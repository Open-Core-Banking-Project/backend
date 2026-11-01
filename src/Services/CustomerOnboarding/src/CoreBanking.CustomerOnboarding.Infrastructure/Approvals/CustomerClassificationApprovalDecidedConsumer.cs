using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CustomerOnboarding.Contracts;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Audit;
using CoreBanking.CustomerOnboarding.Infrastructure.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Approvals;

/// <summary>
/// Applies a Workflow decision to the exact classification version that was submitted. The decision must cite
/// the approval request, the version and the content hash this service froze at submission; anything else is
/// refused. Idempotent through the inbox. Decision: P1-E03-D09.
/// </summary>
public sealed class CustomerClassificationApprovalDecidedConsumer : IIntegrationEventConsumer
{
    public const string SourceProducer = "operations-workflow";
    public const string ConsumerName = "customer-onboarding.classification-approval-decided";

    private readonly CustomerOnboardingDbContext _context;
    private readonly InboxProcessor _inbox;
    private readonly AmbientTenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public CustomerClassificationApprovalDecidedConsumer(
        CustomerOnboardingDbContext context,
        InboxProcessor inbox,
        AmbientTenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public static string TopicName =>
        EventContract.TopicFor(
            SourceProducer,
            CustomerClassificationApprovalV1.EventFamily,
            CustomerClassificationApprovalV1.SchemaVersion);

    public string Topic => TopicName;

    public async Task<InboxResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        _tenantContext.Clear();
        _tenantContext.Establish(InstitutionId.From(envelope.InstitutionId));
        try
        {
            return await _inbox.ProcessAsync(
                ConsumerName,
                envelope,
                CustomerClassificationApprovalV1.SchemaVersion,
                token => ApplyAsync(envelope, token),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _tenantContext.Clear();
        }
    }

    private async Task<string?> ApplyAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!string.Equals(envelope.Producer, SourceProducer, StringComparison.Ordinal)
            || !string.Equals(envelope.EventType, CustomerClassificationApprovalV1.DecidedEventType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The approval topic contained an unexpected producer or event type.");
        }

        CustomerApprovalDecidedV1 payload = EventContract.Deserialize<CustomerApprovalDecidedV1>(envelope.Payload)
            ?? throw new InvalidOperationException("The approval decision payload is missing.");
        Validate(envelope, payload);

        InstitutionId institutionId = InstitutionId.From(payload.InstitutionId);
        CustomerRecord record = await _context.Customers
            .Include(customer => customer.Classifications)
            .SingleOrDefaultAsync(customer => customer.CustomerId == payload.SubjectId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The customer of the approval decision does not exist in this institution.");

        Customer customer = CustomerService.ToModel(record);
        int version = ParseVersion(payload.SubjectVersion);
        CustomerClassification classification = customer.Classifications
            .SingleOrDefault(candidate => candidate.Version == version)
            ?? throw new InvalidOperationException("The classification version of the approval decision does not exist.");

        if (!string.Equals(classification.ApprovalRequestId, payload.RequestId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The approval decision cites a request that this version was not submitted under.");
        }

        if (!string.Equals(classification.ApprovalContentHash, payload.SubjectHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The approval decision cites a content hash that this version does not have.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool approved = string.Equals(payload.Decision, CustomerApprovalDecisions.Approved, StringComparison.Ordinal);
        if (approved)
        {
            try
            {
                customer.ApproveClassification(version, payload.DecisionId, now);
            }
            catch (OnboardingRuleException exception)
                when (string.Equals(exception.Code, "customer_classification_transition_invalid", StringComparison.Ordinal)
                    && string.Equals(classification.WorkflowDecisionReference, payload.DecisionId, StringComparison.Ordinal))
            {
                // The same decision delivered again is not an error and must not change anything.
                return "already_approved:" + payload.DecisionId;
            }

            CustomerService.Apply(customer, record);
        }

        // Approval and application are separate states: the owning service acknowledges what it actually applied.
        CustomerEventWriter.Add(
            _context,
            institutionId,
            payload.RequestId,
            CustomerApprovalApplicationV1.EventType,
            new CustomerApprovalApplicationResultV1(
                institutionId.Value,
                payload.RequestId,
                payload.DecisionId,
                payload.DecisionId,
                payload.WorkflowKey,
                payload.SubjectType,
                payload.SubjectId,
                payload.SubjectVersion,
                payload.SubjectHash,
                CustomerApprovalApplicationV1.SourceService,
                approved ? CustomerApprovalApplicationV1.Applied : CustomerApprovalApplicationV1.Rejected,
                FailureCode: null,
                payload.Reason,
                payload.CorrelationId,
                now),
            payload.CorrelationId,
            now,
            CustomerApprovalApplicationV1.EventFamily);

        AuditRecorder.Record(
            _context,
            institutionId,
            payload.ApproverActorId,
            approved
                ? AuditRecorder.ActionCustomerClassificationApproved
                : AuditRecorder.ActionCustomerClassificationRejected,
            AuditRecorder.SubjectCustomer,
            payload.SubjectId,
            payload.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["version"] = version.ToString(CultureInfo.InvariantCulture),
                ["decisionId"] = payload.DecisionId,
                ["requestId"] = payload.RequestId,
                ["approverActorId"] = payload.ApproverActorId,
                ["reason"] = payload.Reason ?? string.Empty,
            },
            now);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return payload.Decision + ":" + payload.DecisionId;
    }

    private static void Validate(IntegrationEventEnvelope envelope, CustomerApprovalDecidedV1 payload)
    {
        if (!string.Equals(payload.InstitutionId, envelope.InstitutionId, StringComparison.Ordinal)
            || !string.Equals(payload.RequestId, envelope.BusinessReference, StringComparison.Ordinal)
            || !string.Equals(payload.CorrelationId, envelope.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowKey, CustomerClassificationApprovalV1.WorkflowKey, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowVersionId, CustomerClassificationApprovalV1.WorkflowVersionId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowDefinitionHash, CustomerClassificationApprovalV1.DefinitionHash, StringComparison.Ordinal)
            || !string.Equals(payload.SubjectType, CustomerClassificationApprovalV1.SubjectType, StringComparison.Ordinal)
            || !string.Equals(payload.Permission, CustomerClassificationApprovalV1.RequiredPermission, StringComparison.Ordinal)
            || payload.DecidedAt < payload.EntitlementValidFrom
            || payload.DecidedAt >= payload.EntitlementValidUntil
            || !IsDecision(payload.Decision))
        {
            throw new InvalidOperationException(
                "The approval decision does not match the approved profile, the trusted envelope or the decision-time entitlement evidence.");
        }
    }

    private static bool IsDecision(string decision) =>
        string.Equals(decision, CustomerApprovalDecisions.Approved, StringComparison.Ordinal)
        || string.Equals(decision, CustomerApprovalDecisions.Rejected, StringComparison.Ordinal);

    private static int ParseVersion(string subjectVersion) =>
        int.TryParse(subjectVersion, NumberStyles.None, CultureInfo.InvariantCulture, out int version) && version >= 1
            ? version
            : throw new InvalidOperationException("The approval decision does not name a valid classification version.");
}
