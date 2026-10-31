using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Institutions;

/// <summary>
/// Replays the original provisioning fact for a stuck run. A failed downstream step is not
/// repairable here because its owning service must first apply its documented source repair.
/// </summary>
public sealed class InstitutionProvisioningRepairService : IInstitutionProvisioningRepairService
{
    public const string Operation = "platform.institution.provisioning.repair";
    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly InstitutionMetrics _metrics;

    public InstitutionProvisioningRepairService(
        CoreLedgerDbContext context,
        IdempotencyService idempotency,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        InstitutionMetrics? metrics = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _metrics = metrics ?? InstitutionMetrics.Shared;
    }

    public async Task<InstitutionProvisioningStatusResult> GetStatusAsync(
        InstitutionId institutionId,
        string provisioningRunId,
        CancellationToken cancellationToken)
    {
        EnsureTrustedTarget(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(provisioningRunId);

        ProvisioningRunRecord run = await _context.ProvisioningRuns
            .AsNoTracking()
            .Include(record => record.Steps)
            .SingleOrDefaultAsync(record => record.ProvisioningRunId == provisioningRunId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("provisioning_run_not_found", "The provisioning run is not available in this institution.");

        return new InstitutionProvisioningStatusResult(
            run.InstitutionId,
            run.ProvisioningRunId,
            run.PlanVersion,
            run.Status,
            run.StartedAt,
            run.UpdatedAt,
            run.Steps
                .OrderBy(step => step.ServiceId, StringComparer.Ordinal)
                .Select(step => new InstitutionProvisioningStepStatus(
                    step.ServiceId,
                    step.Status,
                    step.FailureCode,
                    step.UpdatedAt))
                .ToArray());
    }

    public async Task<RepairInstitutionProvisioningResult> RepairAsync(
        RepairInstitutionProvisioningCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureTrustedTarget(command.InstitutionId);
        ArgumentNullException.ThrowIfNull(command.ActorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ProvisioningRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        string reason = command.Reason.Trim();
        if (reason.Length > 512)
        {
            throw new ArgumentException("Repair reason cannot exceed 512 characters.", nameof(command));
        }

        string fingerprint = IdempotencyKey.Fingerprint(
            $"{command.InstitutionId.Value}|{command.ProvisioningRunId.Trim()}|{reason}");
        IdempotencyOutcome outcome;
        try
        {
            outcome = await _idempotency.ExecuteAsync(
                IdempotencyKey.Create(
                    command.InstitutionId.Value,
                    Operation,
                    command.ActorId.Value,
                    command.IdempotencyKey),
                fingerprint,
                token => ExecuteRepairAsync(command, reason, token),
                cancellationToken).ConfigureAwait(false);
        }
        catch (InstitutionProvisioningRepairRejectedException)
        {
            _metrics.ProvisioningRepairRejections.Add(1);
            throw;
        }

        return outcome.Kind switch
        {
            IdempotencyOutcomeKind.Executed => Decode(outcome.Result, false),
            IdempotencyOutcomeKind.Replayed => Decode(outcome.Result, true),
            IdempotencyOutcomeKind.Conflict => throw Reject(
                "idempotency_key_reused_with_different_request",
                "The idempotency key was already used with different repair data."),
            _ => throw new InstitutionProvisioningRepairInProgressException(
                "An equivalent provisioning repair request is still in progress."),
        };
    }

    private async Task<string?> ExecuteRepairAsync(
        RepairInstitutionProvisioningCommand command,
        string reason,
        CancellationToken cancellationToken)
    {
        ProvisioningRunRecord run = await _context.ProvisioningRuns
            .Include(record => record.Steps)
            .SingleOrDefaultAsync(
                record => record.ProvisioningRunId == command.ProvisioningRunId.Trim(),
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("provisioning_run_not_found", "The provisioning run is not available in this institution.");

        InstitutionProvisioningRepairPolicy.EnsureReplayEligible(run.Status, run.Steps.Select(step => step.Status));

        OutboxRecord source = await _context.OutboxMessages.SingleOrDefaultAsync(
            record => record.EventType == InstitutionCreationService.EventType
                && record.BusinessReference == command.InstitutionId.Value,
            cancellationToken).ConfigureAwait(false)
            ?? throw Reject("provisioning_source_event_missing", "The original provisioning event is unavailable for safe replay.");

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string auditEventId = Guid.NewGuid().ToString("N");
        string beforeReference = string.Create(
            CultureInfo.InvariantCulture,
            $"run={run.Status};outbox={source.Status};attempts={source.Attempts};published={source.PublishedAt:O};event={source.EventId}");
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        source.Status = OutboxStatus.Pending;
        source.Attempts = 0;
        source.NextAttemptAt = now;
        source.LastError = null;
        source.PublishedAt = null;

        InstitutionRecord institution = await _context.Institutions.SingleAsync(cancellationToken).ConfigureAwait(false);
        institution.LastProvisioningAttemptAt = now;
        institution.UpdatedAt = now;
        institution.Version++;

        await _context.DeadLetters
            .Where(letter => letter.EventId == source.EventId && letter.RepairedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(letter => letter.RepairedAt, now),
                cancellationToken).ConfigureAwait(false);

        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = command.InstitutionId.Value,
            AuditEventId = auditEventId,
            ActorId = command.ActorId.Value,
            ActorType = "human",
            Permission = InstitutionPermissions.ProvisioningRepair.Value,
            Action = "institution.provisioning.replay_requested",
            ResourceType = "provisioning_run",
            ResourceId = run.ProvisioningRunId,
            Outcome = "succeeded",
            Reason = reason,
            CorrelationId = command.CorrelationId,
            BeforeReference = beforeReference,
            AfterReference = $"run={run.Status};outbox=Pending;attempts=0;event={source.EventId}",
            OccurredAt = now,
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InstitutionProvisioningRepairRejectedException(
                "provisioning_repair_conflict",
                "Provisioning state changed while the repair was being requested.",
                exception);
        }

        _metrics.ProvisioningRepairRequests.Add(1);
        return string.Join(
            '|',
            command.InstitutionId.Value,
            run.ProvisioningRunId,
            source.EventId,
            auditEventId,
            now.ToString("O", CultureInfo.InvariantCulture));
    }

    private void EnsureTrustedTarget(InstitutionId institutionId)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        if (_tenantContext.CurrentInstitution != institutionId)
        {
            throw Reject(
                "cross_tenant_access_denied",
                "The authenticated institution does not match the provisioning target.");
        }
    }

    private static RepairInstitutionProvisioningResult Decode(string? value, bool replayed)
    {
        string[] parts = (value ?? string.Empty).Split('|');
        if (parts.Length != 5
            || !DateTimeOffset.TryParseExact(
                parts[4],
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset requestedAt))
        {
            throw new InvalidOperationException("The recorded idempotency outcome is not a provisioning repair outcome.");
        }

        return new RepairInstitutionProvisioningResult(parts[0], parts[1], parts[2], parts[3], requestedAt, replayed);
    }

    private static InstitutionProvisioningRepairRejectedException Reject(string code, string message) => new(code, message);
}
