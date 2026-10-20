using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Institutions;

public sealed class InstitutionSuspensionService : IInstitutionSuspensionService
{
    public const string EventType = "institution_suspended";
    public const string Operation = "platform.institution.suspend";
    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public InstitutionSuspensionService(CoreLedgerDbContext context, IdempotencyService idempotency, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _context = context;
        _idempotency = idempotency;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<SuspendInstitutionResult> SuspendAsync(SuspendInstitutionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Reason);
        if (command.Reason.Trim().Length > 512)
        {
            throw new ArgumentException("Suspension reason cannot exceed 512 characters.", nameof(command));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        if (_tenantContext.CurrentInstitution != command.InstitutionId)
        {
            throw new InstitutionSuspensionRejectedException("cross_tenant_access_denied", "The authenticated institution does not match the suspension target.");
        }

        string fingerprint = IdempotencyKey.Fingerprint($"{command.InstitutionId.Value}|{command.Reason.Trim()}");
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, Operation, command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => ExecuteAsync(command, token),
            cancellationToken).ConfigureAwait(false);
        return outcome.Kind switch
        {
            IdempotencyOutcomeKind.Executed => Decode(outcome.Result, false),
            IdempotencyOutcomeKind.Replayed => Decode(outcome.Result, true),
            IdempotencyOutcomeKind.Conflict => throw new InstitutionSuspensionRejectedException("idempotency_key_reused_with_different_request", "The idempotency key was already used with a different suspension request."),
            _ => throw new InstitutionSuspensionInProgressException("An equivalent institution suspension request is still in progress."),
        };
    }

    private async Task<string?> ExecuteAsync(SuspendInstitutionCommand command, CancellationToken cancellationToken)
    {
        InstitutionRecord institution = await _context.Institutions.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InstitutionSuspensionRejectedException("institution_not_found", "The institution is not available.");
        if (!string.Equals(institution.Status, "active", StringComparison.Ordinal))
        {
            throw new InstitutionSuspensionRejectedException("institution_transition_invalid", "Only an active institution can be suspended.");
        }


        InstitutionSuspensionPolicy.EnsureIndependentApprover(institution.ActivatedByActorId, command.ActorId);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string eventId = Guid.NewGuid().ToString("N");
        string reason = command.Reason.Trim();
        institution.Status = "suspended";
        institution.SuspendedAt = now;
        institution.SuspendedByActorId = command.ActorId.Value;
        institution.SuspensionReason = reason;
        institution.SuspensionCorrelationId = command.CorrelationId;
        institution.UpdatedAt = now;
        institution.Version++;

        InstitutionSuspendedV1 payload = new(institution.InstitutionId, command.ActorId.Value, reason, now);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = EventType,
            SchemaVersion = InstitutionCreationService.SchemaVersion,
            OccurredAt = now,
            InstitutionId = institution.InstitutionId,
            CorrelationId = command.CorrelationId,
            Producer = InstitutionCreationService.Producer,
            BusinessReference = institution.InstitutionId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = institution.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = EventType,
            SchemaVersion = InstitutionCreationService.SchemaVersion,
            Topic = EventContract.TopicFor(InstitutionCreationService.Producer, InstitutionCreationService.EventFamily, InstitutionCreationService.SchemaVersion),
            BusinessReference = institution.InstitutionId,
            CorrelationId = command.CorrelationId,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            Producer = InstitutionCreationService.Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry in _context.ChangeTracker.Entries()
                         .Where(entry => entry.Entity is not IdempotencyRecord && entry.State != EntityState.Unchanged))
            {
                entry.State = EntityState.Detached;
            }
            throw new InstitutionSuspensionRejectedException("institution_transition_conflict", "The institution lifecycle changed while suspension was being processed.", exception);
        }

        return string.Join('|', institution.InstitutionId, eventId, now.ToString("O", CultureInfo.InvariantCulture));
    }

    private static SuspendInstitutionResult Decode(string? value, bool replayed)
    {
        string[] parts = (value ?? string.Empty).Split('|');
        if (parts.Length != 3 || !DateTimeOffset.TryParseExact(parts[2], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset at))
        {
            throw new InvalidOperationException("The recorded idempotency outcome is not an institution suspension outcome.");
        }

        return new SuspendInstitutionResult(parts[0], parts[1], at, replayed);
    }
}
