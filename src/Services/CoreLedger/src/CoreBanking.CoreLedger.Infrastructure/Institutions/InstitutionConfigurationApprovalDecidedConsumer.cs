using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Institutions;

/// <summary>
/// Applies a Workflow decision to the exact submitted institution configuration version. A decision
/// whose profile, subject, hash or entitlement evidence differs is refused before anything changes.
/// Decision: P1-E04-D17.
/// </summary>
public sealed class InstitutionConfigurationApprovalDecidedConsumer : IIntegrationEventConsumer
{
    public const string SourceProducer = "operations-workflow";
    public const string EventFamily = "approval";
    public const string EventType = "approval_decided";
    public const string ConsumerName = "core-ledger.institution-configuration-approval-decided";
    public const int SchemaVersion = 1;

    private const string SubmittedStatus = "submitted";
    private const string ApprovedStatus = "approved";
    private const string RejectedStatus = "draft";

    private readonly CoreLedgerDbContext _context;
    private readonly InboxProcessor _inbox;
    private readonly AmbientTenantContext _tenantContext;

    public InstitutionConfigurationApprovalDecidedConsumer(
        CoreLedgerDbContext context,
        InboxProcessor inbox,
        AmbientTenantContext tenantContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public static string TopicName => EventContract.TopicFor(SourceProducer, EventFamily, SchemaVersion);

    public string Topic => TopicName;

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

        ConfigurationVersionRecord version = await _context.ConfigurationVersions
            .SingleOrDefaultAsync(
                record => record.VersionId == payload.SubjectId
                    && record.InstitutionId == envelope.InstitutionId
                    && record.Schema == InstitutionCurrencySet.Schema,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The submitted configuration version is not available in this institution.");
        if (!string.Equals(version.Status, SubmittedStatus, StringComparison.Ordinal)
            || !string.Equals(version.ContentHash, payload.SubjectHash, StringComparison.Ordinal)
            || !string.Equals(version.VersionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), payload.SubjectVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The approval decision does not match the exact submitted configuration version.");
        }

        bool approved = string.Equals(payload.Decision, "approved", StringComparison.OrdinalIgnoreCase);
        if (!approved && !string.Equals(payload.Decision, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The approval decision is not a supported outcome.");
        }

        version.Status = approved ? ApprovedStatus : RejectedStatus;
        version.ApprovedByActorId = approved ? payload.ApproverActorId : null;
        version.ApprovalReference = approved ? payload.RequestId : null;
        version.Version++;
        return payload.Decision + ":" + version.VersionId;
    }

    private static void Validate(IntegrationEventEnvelope envelope, ApprovalDecidedV1 payload)
    {
        if (!string.Equals(payload.InstitutionId, envelope.InstitutionId, StringComparison.Ordinal)
            || !string.Equals(payload.RequestId, envelope.BusinessReference, StringComparison.Ordinal)
            || !string.Equals(payload.CorrelationId, envelope.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowKey, InstitutionConfigurationApprovalV1.WorkflowKey, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowVersionId, InstitutionConfigurationApprovalV1.WorkflowVersionId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowDefinitionHash, InstitutionConfigurationApprovalV1.WorkflowDefinitionHash, StringComparison.Ordinal)
            || !string.Equals(payload.SubjectType, InstitutionConfigurationApprovalV1.SubjectType, StringComparison.Ordinal)
            || !string.Equals(payload.Permission, InstitutionConfigurationApprovalV1.RequiredPermission, StringComparison.Ordinal)
            || payload.EntitlementValidUntil <= payload.EntitlementValidFrom
            || payload.DecidedAt < payload.EntitlementValidFrom
            || payload.DecidedAt >= payload.EntitlementValidUntil)
        {
            throw new InvalidOperationException("The approval decision does not match the trusted envelope, profile, or decision-time entitlement evidence.");
        }
    }
}
