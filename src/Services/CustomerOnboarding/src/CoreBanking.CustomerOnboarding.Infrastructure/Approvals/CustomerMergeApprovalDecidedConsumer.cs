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
/// Applies a Workflow decision to one merge plan. The decision must cite the merge request and the frozen plan
/// hash; an approval makes the plan applicable and a rejection ends it. Idempotent through the inbox.
/// Decision: P1-E03-D11.
/// </summary>
public sealed class CustomerMergeApprovalDecidedConsumer : IIntegrationEventConsumer
{
    public const string SourceProducer = "operations-workflow";
    public const string ConsumerName = "customer-onboarding.merge-approval-decided";

    private readonly CustomerOnboardingDbContext _context;
    private readonly InboxProcessor _inbox;
    private readonly AmbientTenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public CustomerMergeApprovalDecidedConsumer(
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

        MergeRequestRecord record = await _context.MergeRequests
            .SingleOrDefaultAsync(request => request.MergeRequestId == payload.SubjectId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The merge request of the approval decision does not exist in this institution.");

        if (!string.Equals(record.PlanHash, payload.SubjectHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The approval decision cites a plan hash that this merge request does not have.");
        }

        CustomerMergeRequest request = CustomerMergeService.ToMergeModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool approved = string.Equals(payload.Decision, CustomerApprovalDecisions.Approved, StringComparison.Ordinal);
        string status = MergeCodes.ToCode(request.Status);
        string decisionId = payload.DecisionId;

        if (approved)
        {
            if (string.Equals(status, "approved", StringComparison.Ordinal)
                || string.Equals(status, "applying", StringComparison.Ordinal)
                || string.Equals(status, "reconciliation_required", StringComparison.Ordinal)
                || string.Equals(status, "completed", StringComparison.Ordinal))
            {
                return "already_approved:" + decisionId;
            }

            try
            {
                if (string.Equals(status, "analysed", StringComparison.Ordinal))
                {
                    request.SubmitForApproval(now);
                }

                request.Approve(decisionId, now);
            }
            catch (OnboardingRuleException exception)
            {
                throw new InvalidOperationException("The merge request cannot accept this approval right now.", exception);
            }
        }
        else
        {
            if (MergeCodes.IsTerminal(request.Status))
            {
                return "already_terminal:" + status;
            }

            try
            {
                request.Reject(
                    string.IsNullOrWhiteSpace(payload.Reason) ? "rejected by workflow" : payload.Reason,
                    now);
            }
            catch (OnboardingRuleException exception)
            {
                throw new InvalidOperationException("The merge request cannot be rejected in its current state.", exception);
            }
        }

        CustomerMergeService.ApplyMerge(request, record);

        CustomerEventWriter.Add(
            _context,
            InstitutionId.From(payload.InstitutionId),
            payload.RequestId,
            CustomerApprovalApplicationV1.EventType,
            new CustomerApprovalApplicationResultV1(
                payload.InstitutionId,
                payload.RequestId,
                decisionId,
                decisionId,
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
            InstitutionId.From(payload.InstitutionId),
            payload.ApproverActorId,
            approved ? AuditRecorder.ActionCustomerMergeApproved : AuditRecorder.ActionCustomerMergeRejected,
            "merge_request",
            request.MergeRequestId,
            payload.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["decisionId"] = decisionId,
                ["planHash"] = request.PlanHash,
                ["approverActorId"] = payload.ApproverActorId,
                ["reason"] = payload.Reason ?? string.Empty,
            },
            now);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return payload.Decision + ":" + decisionId;
    }

    private static void Validate(IntegrationEventEnvelope envelope, CustomerApprovalDecidedV1 payload)
    {
        if (!string.Equals(payload.InstitutionId, envelope.InstitutionId, StringComparison.Ordinal)
            || !string.Equals(payload.RequestId, envelope.BusinessReference, StringComparison.Ordinal)
            || !string.Equals(payload.CorrelationId, envelope.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowKey, CustomerMergeApprovalV1.WorkflowKey, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowVersionId, CustomerMergeApprovalV1.WorkflowVersionId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowDefinitionHash, CustomerMergeApprovalV1.DefinitionHash, StringComparison.Ordinal)
            || !string.Equals(payload.SubjectType, CustomerMergeApprovalV1.SubjectType, StringComparison.Ordinal)
            || !string.Equals(payload.Permission, CustomerMergeApprovalV1.RequiredPermission, StringComparison.Ordinal)
            || payload.DecidedAt < payload.EntitlementValidFrom
            || payload.DecidedAt >= payload.EntitlementValidUntil
            || !IsDecision(payload.Decision))
        {
            throw new InvalidOperationException(
                "The approval decision does not match the approved merge profile, the trusted envelope or the entitlement evidence.");
        }
    }

    private static bool IsDecision(string decision) =>
        string.Equals(decision, CustomerApprovalDecisions.Approved, StringComparison.Ordinal)
        || string.Equals(decision, CustomerApprovalDecisions.Rejected, StringComparison.Ordinal);
}
