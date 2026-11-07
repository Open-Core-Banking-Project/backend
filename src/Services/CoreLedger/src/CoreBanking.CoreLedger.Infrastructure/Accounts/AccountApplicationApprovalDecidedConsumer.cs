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
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Applies a Workflow decision to the exact submitted application. An approval opens the account in the same
/// transaction: the number, the account row, the ledger account, the history, the audit and the outbox fact
/// commit together, and a rejection leaves no number assigned. Decision: P1-E04-D09.
/// </summary>
public sealed class AccountApplicationApprovalDecidedConsumer : IIntegrationEventConsumer
{
    public const string SourceProducer = "operations-workflow";
    public const string EventFamily = "approval";
    public const string EventType = "approval_decided";
    public const string ConsumerName = "core-ledger.account-application-approval-decided";
    public const int SchemaVersion = 1;

    private const string PendingApprovalStatus = nameof(AccountApplicationStatus.PendingApproval);

    private readonly CoreLedgerDbContext _context;
    private readonly IAccountOpeningService _opening;
    private readonly InboxProcessor _inbox;
    private readonly AmbientTenantContext _tenantContext;

    public AccountApplicationApprovalDecidedConsumer(
        CoreLedgerDbContext context,
        IAccountOpeningService opening,
        InboxProcessor inbox,
        AmbientTenantContext tenantContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _opening = opening ?? throw new ArgumentNullException(nameof(opening));
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

        bool approved = string.Equals(payload.Decision, "approved", StringComparison.OrdinalIgnoreCase);
        if (!approved && !string.Equals(payload.Decision, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The approval decision is not a supported outcome.");
        }

        AccountApplicationRecord application = await _context.AccountApplications
            .SingleOrDefaultAsync(
                record => record.AccountApplicationId == payload.SubjectId
                    && record.InstitutionId == envelope.InstitutionId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The submitted application is not available in this institution.");

        if (!string.Equals(application.ApprovalRequestId, payload.RequestId, StringComparison.Ordinal)
            || !string.Equals(application.EligibilitySnapshotHash, payload.SubjectHash, StringComparison.Ordinal)
            || !string.Equals(
                application.Version.ToString(CultureInfo.InvariantCulture),
                payload.SubjectVersion,
                StringComparison.Ordinal)
            || !string.Equals(application.Status, PendingApprovalStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The approval decision does not match the exact submitted application version and snapshot.");
        }

        DateTimeOffset decidedAt = payload.DecidedAt;
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        application.Status = AccountApplicationLifecycle
            .RecordApprovalDecision(ParseStatus(application.Status), approved)
            .ToString();
        application.ApprovedByActorId = approved ? payload.ApproverActorId : null;
        application.ApprovedAt = approved ? decidedAt : null;
        application.Version++;
        application.UpdatedAt = decidedAt;

        string result = payload.Decision + ":" + application.AccountApplicationId;
        if (approved)
        {
            // The account, its number and its ledger account commit with the approval decision.
            await _opening.OpenWithinTransactionAsync(
                new OpenAccountCommand(
                    InstitutionId.From(envelope.InstitutionId),
                    ActorId.From(payload.ApproverActorId),
                    "approval:" + payload.RequestId,
                    application.AccountApplicationId,
                    application.Version,
                    payload.CorrelationId),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static void Validate(IntegrationEventEnvelope envelope, ApprovalDecidedV1 payload)
    {
        if (!string.Equals(payload.InstitutionId, envelope.InstitutionId, StringComparison.Ordinal)
            || !string.Equals(payload.RequestId, envelope.BusinessReference, StringComparison.Ordinal)
            || !string.Equals(payload.CorrelationId, envelope.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowKey, AccountOpeningApprovalV1.WorkflowKey, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowVersionId, AccountOpeningApprovalV1.WorkflowVersionId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowDefinitionHash, AccountOpeningApprovalV1.WorkflowDefinitionHash, StringComparison.Ordinal)
            || !string.Equals(payload.SubjectType, AccountOpeningApprovalV1.SubjectType, StringComparison.Ordinal)
            || !string.Equals(payload.Permission, AccountOpeningApprovalV1.RequiredPermission, StringComparison.Ordinal)
            || payload.EntitlementValidUntil <= payload.EntitlementValidFrom
            || payload.DecidedAt < payload.EntitlementValidFrom
            || payload.DecidedAt >= payload.EntitlementValidUntil)
        {
            throw new InvalidOperationException("The approval decision does not match the trusted envelope, profile, or decision-time entitlement evidence.");
        }
    }

    private static AccountApplicationStatus ParseStatus(string status) =>
        Enum.TryParse(status, ignoreCase: false, out AccountApplicationStatus parsed)
            ? parsed
            : throw new InvalidOperationException($"account_application_status_unknown:{status}");
}
