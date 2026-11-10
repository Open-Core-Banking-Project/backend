using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Submits an eligible application for approval: the request is bound to the exact application version and
/// eligibility snapshot hash, and Workflow approval is required before any number or account exists.
/// Decision: P1-E04-D09.
/// </summary>
public sealed class AccountApprovalService : IAccountApprovalService
{
    private const string Producer = "core-ledger";
    private const string ApprovalEventFamily = "approval";
    private const string ApprovalRequestedEventType = "approval_requested";
    private const int ApprovalSchemaVersion = 1;
    private const string EligibleStatus = nameof(AccountApplicationStatus.Eligible);

    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public AccountApprovalService(
        CoreLedgerDbContext context,
        IdempotencyService idempotency,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<SubmitAccountApplicationForApprovalResult> SubmitForApprovalAsync(
        SubmitAccountApplicationForApprovalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.InstitutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ApplicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SourceRequestKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        if (_tenantContext.CurrentInstitution != command.InstitutionId)
        {
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the application target.");
        }

        string applicationId = command.ApplicationId.Trim();
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            applicationId, command.ExpectedVersion, command.RequestId.Trim(), command.SourceRequestKey.Trim()));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.application.approval.submit", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => SubmitCoreAsync(command, applicationId, token),
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(outcome.Result))
        {
            throw new InvalidOperationException("The approval submission did not produce an idempotent result.");
        }

        return System.Text.Json.JsonSerializer.Deserialize<SubmitAccountApplicationForApprovalResult>(outcome.Result)
            ?? throw new InvalidOperationException("The stored approval submission evidence is not readable.");
    }

    private async Task<string?> SubmitCoreAsync(
        SubmitAccountApplicationForApprovalCommand command,
        string applicationId,
        CancellationToken cancellationToken)
    {
        AccountApplicationRecord application = await _context.AccountApplications
            .SingleOrDefaultAsync(record => record.AccountApplicationId == applicationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_application_not_found", "The account application is not available in this institution.");

        if (command.ExpectedVersion != application.Version)
        {
            throw Reject("account_application_version_mismatch", "The application version does not match the submitted expectation.");
        }

        if (!string.Equals(application.Status, EligibleStatus, StringComparison.Ordinal))
        {
            throw Reject("account_application_not_eligible", "Only an eligible application can be submitted for approval.");
        }

        AccountEligibilityEvaluationRecord evaluation = await _context.AccountEligibilityEvaluations
            .AsNoTracking()
            .Where(record => record.AccountApplicationId == applicationId)
            .OrderByDescending(record => record.EvaluatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_eligibility_missing", "The eligible application has no recorded evaluation evidence.");
        if (!evaluation.Eligible || !evaluation.FactsCurrent)
        {
            throw Reject("account_eligibility_not_current", "The recorded evaluation is not an eligible, current decision.");
        }

        string snapshotHash = SnapshotHash(evaluation);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string eventId = Guid.NewGuid().ToString("N");
        ApprovalRequestedV1 payload = new(
            command.InstitutionId.Value,
            command.RequestId.Trim(),
            command.SourceRequestKey.Trim(),
            AccountOpeningApprovalV1.WorkflowKey,
            AccountOpeningApprovalV1.WorkflowVersionId,
            AccountOpeningApprovalV1.WorkflowDefinitionHash,
            AccountOpeningApprovalV1.SourceService,
            AccountOpeningApprovalV1.SubjectType,
            application.AccountApplicationId,
            application.Version.ToString(CultureInfo.InvariantCulture),
            snapshotHash,
            command.ActorId.Value,
            AccountOpeningApprovalV1.RequiredPermission,
            command.CorrelationId,
            now);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = ApprovalRequestedEventType,
            SchemaVersion = ApprovalSchemaVersion,
            OccurredAt = now,
            InstitutionId = command.InstitutionId.Value,
            CorrelationId = command.CorrelationId,
            Producer = Producer,
            BusinessReference = command.RequestId.Trim(),
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = command.InstitutionId.Value,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = ApprovalRequestedEventType,
            SchemaVersion = ApprovalSchemaVersion,
            Topic = EventContract.TopicFor(Producer, ApprovalEventFamily, ApprovalSchemaVersion),
            BusinessReference = command.RequestId.Trim(),
            CorrelationId = command.CorrelationId,
            TraceParent = System.Diagnostics.Activity.Current?.Id,
            TraceState = System.Diagnostics.Activity.Current?.TraceStateString,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });

        application.Status = AccountApplicationLifecycle
            .RecordSubmittedForApproval(ParseStatus(application.Status))
            .ToString();
        application.EligibilityEvaluationId = evaluation.EvaluationId;
        application.EligibilitySnapshotHash = snapshotHash;
        application.ApprovalRequestId = command.RequestId.Trim();
        application.Version++;
        application.UpdatedAt = now;

        AddAudit(command, now);
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            throw new AccountApplicationRejectedException(
                "account_approval_submission_conflict",
                "The application changed while approval was being submitted.",
                exception);
        }

        return System.Text.Json.JsonSerializer.Serialize(new SubmitAccountApplicationForApprovalResult(
            application.AccountApplicationId,
            command.RequestId.Trim(),
            evaluation.EvaluationId,
            snapshotHash,
            application.Status,
            application.Version,
            eventId,
            now));
    }

    /// <summary>
    /// The decision is bound to this hash, so a later evaluation can never be substituted for the one the
    /// approver saw. Decision: P1-E04-D09.
    /// </summary>
    private static string SnapshotHash(AccountEligibilityEvaluationRecord evaluation) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            "account-eligibility-snapshot.v1",
            evaluation.EvaluationId,
            evaluation.ProductVersionId,
            evaluation.ProductContentHash,
            evaluation.CustomerFactsVersion,
            evaluation.BranchReferenceVersion,
            evaluation.CurrencyReferenceVersion,
            evaluation.DecisionCode,
            evaluation.EvaluatedAt.ToString("O", CultureInfo.InvariantCulture)))))
            .ToLowerInvariant();

    private void AddAudit(SubmitAccountApplicationForApprovalCommand command, DateTimeOffset now)
    {
        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = command.InstitutionId.Value,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = command.ActorId.Value,
            ActorType = "human",
            Permission = AccountPermissions.ApplicationCreate.Value,
            Action = "account.application.approval.submit",
            ResourceType = "account.application",
            ResourceId = command.ApplicationId.Trim(),
            Outcome = "succeeded",
            Reason = string.Empty,
            CorrelationId = command.CorrelationId,
            AfterReference = command.RequestId.Trim(),
            OccurredAt = now,
        });
    }

    private static AccountApplicationStatus ParseStatus(string status) =>
        Enum.TryParse(status, ignoreCase: false, out AccountApplicationStatus parsed)
            ? parsed
            : throw new InvalidOperationException($"account_application_status_unknown:{status}");

    private static AccountApplicationRejectedException Reject(string code, string message) => new(code, message);
}
