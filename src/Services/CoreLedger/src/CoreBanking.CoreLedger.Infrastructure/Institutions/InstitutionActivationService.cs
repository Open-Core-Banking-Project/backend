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

public sealed class InstitutionActivationService : IInstitutionActivationService
{
    public const string EventType = "institution_activated";
    public const string Operation = "platform.institution.activate";

    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public InstitutionActivationService(
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

    public async Task<ActivateInstitutionResult> ActivateAsync(
        ActivateInstitutionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.ActorId);
        if (_tenantContext.CurrentInstitution != command.InstitutionId)
        {
            throw new InstitutionActivationRejectedException(
                "cross_tenant_access_denied",
                "The authenticated institution does not match the activation target.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        string fingerprint = IdempotencyKey.Fingerprint(command.InstitutionId.Value);
        IdempotencyKey key = IdempotencyKey.Create(
            command.InstitutionId.Value,
            Operation,
            command.ActorId.Value,
            command.IdempotencyKey);

        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            key,
            fingerprint,
            token => ExecuteActivationAsync(command, token),
            cancellationToken).ConfigureAwait(false);

        return outcome.Kind switch
        {
            IdempotencyOutcomeKind.Executed => Decode(outcome, replayed: false),
            IdempotencyOutcomeKind.Replayed => Decode(outcome, replayed: true),
            IdempotencyOutcomeKind.Conflict => throw new InstitutionActivationRejectedException(
                "idempotency_key_reused_with_different_request",
                "The idempotency key was already used with a different activation request."),
            _ => throw new InstitutionActivationInProgressException(
                "An equivalent institution activation request is still in progress."),
        };
    }

    private async Task<string?> ExecuteActivationAsync(
        ActivateInstitutionCommand command,
        CancellationToken cancellationToken)
    {
        InstitutionRecord institution = await _context.Institutions.SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InstitutionActivationRejectedException(
                "institution_not_provisioned",
                "The institution is not available for activation.");

        bool isReactivation = string.Equals(institution.Status, "suspended", StringComparison.Ordinal);
        InstitutionActivationPolicy.EnsureIndependentApprover(
            isReactivation ? institution.SuspendedByActorId : institution.CreatedByActorId,
            command.ActorId);

        ProvisioningRunRecord run = await _context.ProvisioningRuns
            .Include(record => record.Steps)
            .SingleOrDefaultAsync(
                record => record.Status == (isReactivation ? "completed" : "pending_activation")
                    && record.PlanVersion == institution.ProvisioningPlanVersion,
                cancellationToken).ConfigureAwait(false)
            ?? throw new InstitutionActivationRejectedException(
                "institution_not_provisioned",
                "No completed provisioning run is available for activation.");

        InstitutionActivationPolicy.EnsureProvisioningComplete(
            institution.Status,
            run.Steps.Select(step => step.Status));

        DateTimeOffset activatedAt = _timeProvider.GetUtcNow();
        string eventId = Guid.NewGuid().ToString("N");
        institution.Status = "active";
        institution.ActivatedAt = activatedAt;
        institution.ActivatedByActorId = command.ActorId.Value;
        institution.ActivationCorrelationId = command.CorrelationId;
        institution.UpdatedAt = activatedAt;
        institution.Version++;
        institution.SuspendedAt = null;
        if (!isReactivation)
        {
            run.Status = "completed";
            run.CompletedAt = activatedAt;
            run.UpdatedAt = activatedAt;
        }

        InstitutionActivatedV1 payload = new(
            institution.InstitutionId,
            run.ProvisioningRunId,
            run.PlanVersion,
            command.ActorId.Value,
            activatedAt);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = EventType,
            SchemaVersion = InstitutionCreationService.SchemaVersion,
            OccurredAt = activatedAt,
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
            Topic = EventContract.TopicFor(
                InstitutionCreationService.Producer,
                InstitutionCreationService.EventFamily,
                InstitutionCreationService.SchemaVersion),
            BusinessReference = institution.InstitutionId,
            CorrelationId = command.CorrelationId,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            Producer = InstitutionCreationService.Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = activatedAt,
            CreatedAt = activatedAt,
        });

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InstitutionActivationRejectedException(
                "institution_transition_conflict",
                "The institution lifecycle changed while activation was being processed.",
                exception);
        }

        return new ActivatedOutcome(
            institution.InstitutionId,
            run.ProvisioningRunId,
            eventId,
            activatedAt).Encode();
    }

    private static ActivateInstitutionResult Decode(IdempotencyOutcome outcome, bool replayed)
    {
        ActivatedOutcome activated = ActivatedOutcome.Decode(outcome.Result);
        return new ActivateInstitutionResult(
            activated.InstitutionId,
            activated.ProvisioningRunId,
            activated.EventId,
            activated.ActivatedAt,
            replayed);
    }

    private sealed record ActivatedOutcome(
        string InstitutionId,
        string ProvisioningRunId,
        string EventId,
        DateTimeOffset ActivatedAt)
    {
        public string Encode() => string.Join(
            '|',
            InstitutionId,
            ProvisioningRunId,
            EventId,
            ActivatedAt.ToString("O", CultureInfo.InvariantCulture));

        public static ActivatedOutcome Decode(string? value)
        {
            string[] parts = (value ?? string.Empty).Split('|');
            if (parts.Length != 4
                || !DateTimeOffset.TryParseExact(
                    parts[3],
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset activatedAt))
            {
                throw new InvalidOperationException("The recorded idempotency outcome is not an institution activation outcome.");
            }

            return new ActivatedOutcome(parts[0], parts[1], parts[2], activatedAt);
        }
    }
}
