using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Contracts;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Applies a Workflow decision to the exact submitted closure proposal. An approval moves the account to pending
/// closure and runs the final condition re-check inside the same transaction, so a closed account always commits
/// with its status history, audit and outbox fact. Decision: P1-E04-D13.
/// </summary>
public sealed class AccountClosureApprovalDecidedConsumer : IIntegrationEventConsumer
{
    public const string SourceProducer = "operations-workflow";
    public const string EventType = "approval_decided";
    public const string ConsumerName = "core-ledger.account-closure-approval-decided";
    public const int SchemaVersion = 1;

    private readonly IAccountClosureApprovalApplier _applier;
    private readonly InboxProcessor _inbox;
    private readonly AmbientTenantContext _tenantContext;

    public AccountClosureApprovalDecidedConsumer(
        IAccountClosureApprovalApplier applier,
        InboxProcessor inbox,
        AmbientTenantContext tenantContext)
    {
        _applier = applier ?? throw new ArgumentNullException(nameof(applier));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public string Topic => AccountApplicationApprovalDecidedConsumer.TopicName;

    public async Task<InboxResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _tenantContext.Clear();
        _tenantContext.Establish(InstitutionId.From(envelope.InstitutionId));
        try
        {
            return await _inbox.ProcessAsync(
                ConsumerName, envelope, SchemaVersion, token => ApplyAsync(envelope, token), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _tenantContext.Clear();
        }
    }

    private async Task<string?> ApplyAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!string.Equals(envelope.Producer, SourceProducer, StringComparison.Ordinal)
            || !string.Equals(envelope.EventType, EventType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The approval decision topic contained an unexpected producer or event type.");
        }

        ApprovalDecidedV1 payload = EventContract.Deserialize<ApprovalDecidedV1>(envelope.Payload)
            ?? throw new InvalidOperationException("The approval decision payload is missing.");
        Validate(envelope, payload);

        bool approved = string.Equals(payload.Decision, "approved", StringComparison.OrdinalIgnoreCase);
        if (!approved && !string.Equals(payload.Decision, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The approval decision is not a supported outcome.");
        }

        return await _applier.ApplyDecisionAsync(
            InstitutionId.From(envelope.InstitutionId),
            payload.SubjectId,
            payload.RequestId,
            payload.SubjectVersion,
            payload.SubjectHash,
            approved,
            payload.ApproverActorId,
            payload.DecidedAt,
            payload.CorrelationId,
            cancellationToken).ConfigureAwait(false);
    }

    private static void Validate(IntegrationEventEnvelope envelope, ApprovalDecidedV1 payload)
    {
        if (!string.Equals(payload.InstitutionId, envelope.InstitutionId, StringComparison.Ordinal)
            || !string.Equals(payload.RequestId, envelope.BusinessReference, StringComparison.Ordinal)
            || !string.Equals(payload.CorrelationId, envelope.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowKey, AccountClosureApprovalV1.WorkflowKey, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowVersionId, AccountClosureApprovalV1.WorkflowVersionId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowDefinitionHash, AccountClosureApprovalV1.WorkflowDefinitionHash, StringComparison.Ordinal)
            || !string.Equals(payload.SubjectType, AccountClosureApprovalV1.SubjectType, StringComparison.Ordinal)
            || !string.Equals(payload.Permission, AccountClosureApprovalV1.RequiredPermission, StringComparison.Ordinal)
            || payload.EntitlementValidUntil <= payload.EntitlementValidFrom
            || payload.DecidedAt < payload.EntitlementValidFrom
            || payload.DecidedAt >= payload.EntitlementValidUntil
            || !long.TryParse(payload.SubjectVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidOperationException("The approval decision does not match the trusted envelope, profile, or decision-time entitlement evidence.");
        }
    }
}
