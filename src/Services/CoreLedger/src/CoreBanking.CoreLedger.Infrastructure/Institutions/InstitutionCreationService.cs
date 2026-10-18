using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Application.Provisioning;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoreBanking.CoreLedger.Infrastructure.Institutions;

/// <summary>
/// Creates the institution, provisioning run, required steps and outbox event in one local
/// transaction. Publication happens later through the outbox relay (ADR-0007).
/// </summary>
public sealed class InstitutionCreationService : IInstitutionCreationService
{
    public const string Producer = "core-ledger";
    public const string EventFamily = "institution";
    public const string EventType = "institution_provisioning_started";
    public const int SchemaVersion = 1;
    public const string Operation = "platform.institution.create";

    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TenantProvisioningPlanOptions _provisioningPlan;
    private readonly TimeProvider _timeProvider;

    public InstitutionCreationService(
        CoreLedgerDbContext context,
        IdempotencyService idempotency,
        ITenantContext tenantContext,
        TenantProvisioningPlanOptions provisioningPlan,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _provisioningPlan = provisioningPlan ?? throw new ArgumentNullException(nameof(provisioningPlan));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CreateInstitutionResult> CreateAsync(
        CreateInstitutionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureTrustedTarget(command.InstitutionId);
        ArgumentNullException.ThrowIfNull(command.ActorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        Institution institution = Institution.Create(
            command.InstitutionId,
            command.Code,
            command.LegalName,
            command.CountryCode,
            command.BaseCurrency,
            now);
        TenantProvisioning provisioning = _provisioningPlan.CreateFor(command.InstitutionId);

        string fingerprint = IdempotencyKey.Fingerprint(EventContract.Serialize(new InstitutionCreationFingerprint(
            institution.Id.Value,
            institution.Code,
            institution.LegalName,
            institution.CountryCode,
            institution.BaseCurrency)));
        IdempotencyKey key = IdempotencyKey.Create(
            command.InstitutionId.Value,
            Operation,
            command.ActorId.Value,
            command.IdempotencyKey);

        try
        {
            IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
                key,
                fingerprint,
                token => ExecuteCreationAsync(command, institution, provisioning, token),
                cancellationToken).ConfigureAwait(false);

            return outcome.Kind switch
            {
                IdempotencyOutcomeKind.Executed => Decode(outcome, replayed: false),
                IdempotencyOutcomeKind.Replayed => Decode(outcome, replayed: true),
                IdempotencyOutcomeKind.Conflict => throw new InstitutionCreationConflictException(
                    "idempotency_key_reused_with_different_request",
                    "The idempotency key was already used with a materially different institution request."),
                _ => throw new InstitutionCreationInProgressException(
                    "An equivalent institution creation request is still in progress."),
            };
        }
        catch (DbUpdateException exception) when (IsInstitutionCodeConflict(exception))
        {
            throw new InstitutionCreationConflictException(
                "institution_code_conflict",
                "The institution code is already in use.",
                exception);
        }
    }

    private async Task<string?> ExecuteCreationAsync(
        CreateInstitutionCommand command,
        Institution institution,
        TenantProvisioning provisioning,
        CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = _timeProvider.GetUtcNow();
        institution.BeginProvisioning(provisioning, startedAt);

        string provisioningRunId = Guid.NewGuid().ToString("N");
        string eventId = Guid.NewGuid().ToString("N");
        InstitutionRecord institutionRecord = Map(institution);
        ProvisioningRunRecord run = new()
        {
            InstitutionId = institution.Id.Value,
            ProvisioningRunId = provisioningRunId,
            PlanVersion = provisioning.PlanVersion,
            Status = "provisioning",
            StartedAt = startedAt,
            UpdatedAt = startedAt,
        };

        foreach (string serviceId in provisioning.ServiceStatuses.Keys)
        {
            bool isLocalBoundary = string.Equals(serviceId, Producer, StringComparison.Ordinal);
            run.Steps.Add(new ProvisioningStepRecord
            {
                InstitutionId = institution.Id.Value,
                ProvisioningRunId = provisioningRunId,
                ServiceId = serviceId,
                Status = isLocalBoundary ? "initialised" : "pending",
                AcknowledgementEventId = isLocalBoundary ? eventId : null,
                AcknowledgedAt = isLocalBoundary ? startedAt : null,
                UpdatedAt = startedAt,
            });
        }

        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = EventType,
            SchemaVersion = SchemaVersion,
            OccurredAt = startedAt,
            InstitutionId = institution.Id.Value,
            CorrelationId = command.CorrelationId,
            Producer = Producer,
            BusinessReference = institution.Id.Value,
            Payload = EventContract.Serialize(new InstitutionProvisioningStartedV1(
                institution.Id.Value,
                provisioning.PlanVersion)),
        };

        OutboxRecord outbox = new()
        {
            InstitutionId = institution.Id.Value,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = EventType,
            SchemaVersion = SchemaVersion,
            Topic = EventContract.TopicFor(Producer, EventFamily, SchemaVersion),
            BusinessReference = institution.Id.Value,
            CorrelationId = command.CorrelationId,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = startedAt,
            CreatedAt = startedAt,
        };

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        _context.Institutions.Add(institutionRecord);
        _context.ProvisioningRuns.Add(run);
        _context.OutboxMessages.Add(outbox);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            DetachFailedCreation(institutionRecord, run, outbox);
            throw;
        }

        return new CreatedOutcome(institution.Id.Value, provisioningRunId, eventId).Encode();
    }

    private void EnsureTrustedTarget(InstitutionId target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_tenantContext.CurrentInstitution != target)
        {
            throw new InvalidOperationException("Institution creation requires a trusted context matching the target institution.");
        }
    }

    private static InstitutionRecord Map(Institution institution) => new()
    {
        InstitutionId = institution.Id.Value,
        Code = institution.Code,
        LegalName = institution.LegalName,
        CountryCode = institution.CountryCode,
        BaseCurrency = institution.BaseCurrency,
        Status = "provisioning",
        CreatedAt = institution.CreatedAt,
        UpdatedAt = institution.UpdatedAt,
        ProvisioningStartedAt = institution.ProvisioningStartedAt,
        LastProvisioningAttemptAt = institution.LastProvisioningAttemptAt,
        ProvisioningPlanVersion = institution.ProvisioningPlanVersion,
    };

    private void DetachFailedCreation(
        InstitutionRecord institution,
        ProvisioningRunRecord run,
        OutboxRecord outbox)
    {
        foreach (ProvisioningStepRecord step in run.Steps)
        {
            _context.Entry(step).State = EntityState.Detached;
        }

        _context.Entry(run).State = EntityState.Detached;
        _context.Entry(institution).State = EntityState.Detached;
        _context.Entry(outbox).State = EntityState.Detached;
    }

    private static bool IsInstitutionCodeConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_institutions_code",
        };

    private static CreateInstitutionResult Decode(IdempotencyOutcome outcome, bool replayed)
    {
        CreatedOutcome created = CreatedOutcome.Decode(outcome.Result);
        return new CreateInstitutionResult(
            created.InstitutionId,
            created.ProvisioningRunId,
            created.EventId,
            replayed);
    }

    private sealed record InstitutionCreationFingerprint(
        string InstitutionId,
        string Code,
        string LegalName,
        string CountryCode,
        string BaseCurrency);

    private sealed record CreatedOutcome(string InstitutionId, string ProvisioningRunId, string EventId)
    {
        public string Encode() => InstitutionId + "|" + ProvisioningRunId + "|" + EventId;

        public static CreatedOutcome Decode(string? value)
        {
            string[] parts = (value ?? string.Empty).Split('|');
            if (parts.Length != 3)
            {
                throw new InvalidOperationException("The recorded idempotency outcome is not an institution creation outcome.");
            }

            return new CreatedOutcome(parts[0], parts[1], parts[2]);
        }
    }
}
