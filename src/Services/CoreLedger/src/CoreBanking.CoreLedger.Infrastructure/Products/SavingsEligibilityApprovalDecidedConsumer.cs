using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Products;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Products;

public sealed class SavingsEligibilityApprovalDecidedConsumer : IIntegrationEventConsumer
{
    public const string SourceProducer = "operations-workflow";
    public const string EventFamily = "approval";
    public const string EventType = "approval_decided";
    public const string ConsumerName = "core-ledger.savings-eligibility-approval-decided";
    public const int SchemaVersion = 1;

    private readonly CoreLedgerDbContext _context;
    private readonly InboxProcessor _inbox;
    private readonly AmbientTenantContext _tenantContext;

    public SavingsEligibilityApprovalDecidedConsumer(
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
            throw new InvalidOperationException("The approval decision topic contained an unexpected producer or event type.");
        ApprovalDecidedV1 payload = EventContract.Deserialize<ApprovalDecidedV1>(envelope.Payload)
            ?? throw new InvalidOperationException("The approval decision payload is missing.");
        Validate(envelope, payload);

        ConfigurationVersionRecord version = await _context.ConfigurationVersions
            .SingleOrDefaultAsync(x => x.VersionId == payload.SubjectId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The approved product version is not available in this institution.");
        string nextStatus;
        try
        {
            nextStatus = SavingsEligibilityApprovalApplication.Apply(
                new SavingsEligibilityApprovalBinding(
                    version.Status, version.ApprovalReference ?? string.Empty, version.VersionId,
                    version.VersionNumber, version.ContentHash),
                payload.RequestId, payload.SubjectId, payload.SubjectVersion, payload.SubjectHash, payload.Decision);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException("The approval decision does not match the exact submitted product version.", exception);
        }

        version.Status = nextStatus;
        if (string.Equals(nextStatus, "approved", StringComparison.Ordinal))
        {
            version.ApprovedByActorId = payload.ApproverActorId;
        }
        else
        {
            version.ApprovedByActorId = null;
        }

        version.Version++;
        return payload.Decision + ":" + version.VersionId;
    }

    private static void Validate(IntegrationEventEnvelope envelope, ApprovalDecidedV1 payload)
    {
        if (!string.Equals(payload.InstitutionId, envelope.InstitutionId, StringComparison.Ordinal)
            || !string.Equals(payload.RequestId, envelope.BusinessReference, StringComparison.Ordinal)
            || !string.Equals(payload.CorrelationId, envelope.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowKey, SavingsEligibilityApprovalV1.WorkflowKey, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowVersionId, SavingsEligibilityApprovalV1.WorkflowVersionId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowDefinitionHash, SavingsEligibilityApprovalV1.WorkflowDefinitionHash, StringComparison.Ordinal)
            || !string.Equals(payload.SubjectType, SavingsEligibilityApprovalV1.SubjectType, StringComparison.Ordinal)
            || !string.Equals(payload.Permission, SavingsEligibilityApprovalV1.RequiredPermission, StringComparison.Ordinal)
            || payload.EntitlementValidUntil <= payload.EntitlementValidFrom
            || payload.DecidedAt < payload.EntitlementValidFrom
            || payload.DecidedAt >= payload.EntitlementValidUntil)
            throw new InvalidOperationException("The approval decision does not match the trusted envelope, profile, or decision-time entitlement evidence.");
    }
}
