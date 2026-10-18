using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Institutions;

/// <summary>
/// Applies one service-owned provisioning acknowledgement to Core Ledger's authoritative run.
/// Completion means every configured boundary is ready; activation remains a separate decision.
/// </summary>
public sealed class InstitutionBoundaryAcknowledgementConsumer : IIntegrationEventConsumer
{
    public const string EventFamily = "institution";
    public const string SuccessEventType = "institution_boundary_initialised";
    public const string FailureEventType = "institution_boundary_initialisation_failed";
    public const string ConsumerName = "core-ledger.institution-boundary-acknowledgement";
    public const int SchemaVersion = 1;

    private readonly CoreLedgerDbContext _context;
    private readonly InboxProcessor _inbox;
    private readonly AmbientTenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public InstitutionBoundaryAcknowledgementConsumer(
        CoreLedgerDbContext context,
        InboxProcessor inbox,
        AmbientTenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public static string TopicForService(string serviceId) =>
        EventContract.TopicFor(serviceId, EventFamily, SchemaVersion);

    public string Topic => "multiple";

    public async Task<InboxResult> HandleAsync(
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _tenantContext.Clear();
        _tenantContext.Establish(InstitutionId.From(envelope.InstitutionId));

        try
        {
            return await _inbox.ProcessAsync(
                ConsumerName,
                envelope,
                SchemaVersion,
                token => ApplyAcknowledgementAsync(envelope, token),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _tenantContext.Clear();
        }
    }

    private async Task<string?> ApplyAcknowledgementAsync(
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ValidateEnvelope(envelope);

        Acknowledgement acknowledgement = ReadAcknowledgement(envelope);
        ValidateAcknowledgement(envelope, acknowledgement);

        bool sourceExists = await _context.OutboxMessages.AnyAsync(
            record => record.EventId == acknowledgement.SourceEventId
                && record.EventType == InstitutionCreationService.EventType,
            cancellationToken).ConfigureAwait(false);
        if (!sourceExists)
        {
            throw new InvalidOperationException("The acknowledgement does not reference a local provisioning event.");
        }

        ProvisioningRunRecord run = await _context.ProvisioningRuns
            .Include(record => record.Steps)
            .SingleOrDefaultAsync(
                record => record.PlanVersion == acknowledgement.ProvisioningPlanVersion
                    && (record.Status == "provisioning" || record.Status == "provisioning_failed"),
                cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No matching active provisioning run exists.");

        ProvisioningStepRecord step = run.Steps.SingleOrDefault(
            record => string.Equals(record.ServiceId, acknowledgement.ServiceId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The acknowledging service is not part of the provisioning plan.");

        if (string.Equals(step.AcknowledgementEventId, envelope.EventId, StringComparison.Ordinal))
        {
            return "already_applied:" + acknowledgement.ServiceId;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        step.AcknowledgementEventId = envelope.EventId;
        step.AcknowledgedAt = now;
        step.UpdatedAt = now;

        InstitutionRecord institution = await _context.Institutions.SingleAsync(cancellationToken).ConfigureAwait(false);
        if (acknowledgement.IsFailure)
        {
            step.Status = "failed";
            step.FailureCode = acknowledgement.FailureCode;
            run.Status = "provisioning_failed";
            run.UpdatedAt = now;
            institution.Status = "provisioning_failed";
            institution.ProvisioningFailedAt = now;
            institution.UpdatedAt = now;
            return "failed:" + acknowledgement.ServiceId + ":" + acknowledgement.FailureCode;
        }

        step.Status = "initialised";
        step.FailureCode = null;
        run.UpdatedAt = now;

        if (run.Steps.All(record => string.Equals(record.Status, "initialised", StringComparison.Ordinal)))
        {
            run.Status = "pending_activation";
            run.CompletedAt = now;
            institution.Status = "pending_activation";
            institution.ReadyForActivationAt = now;
            institution.ProvisioningFailedAt = null;
            institution.UpdatedAt = now;
        }

        return "initialised:" + acknowledgement.ServiceId;
    }

    private static Acknowledgement ReadAcknowledgement(IntegrationEventEnvelope envelope)
    {
        if (string.Equals(envelope.EventType, SuccessEventType, StringComparison.Ordinal))
        {
            InstitutionBoundaryInitialisedV1 payload =
                EventContract.Deserialize<InstitutionBoundaryInitialisedV1>(envelope.Payload)
                ?? throw new InvalidOperationException("The acknowledgement payload is missing.");
            return new Acknowledgement(
                payload.InstitutionId,
                payload.ServiceId,
                payload.ProvisioningPlanVersion,
                payload.SourceEventId,
                null);
        }

        InstitutionBoundaryInitialisationFailedV1 failure =
            EventContract.Deserialize<InstitutionBoundaryInitialisationFailedV1>(envelope.Payload)
            ?? throw new InvalidOperationException("The failure acknowledgement payload is missing.");
        return new Acknowledgement(
            failure.InstitutionId,
            failure.ServiceId,
            failure.ProvisioningPlanVersion,
            failure.SourceEventId,
            failure.FailureCode);
    }

    private static void ValidateEnvelope(IntegrationEventEnvelope envelope)
    {
        bool supportedType = string.Equals(envelope.EventType, SuccessEventType, StringComparison.Ordinal)
            || string.Equals(envelope.EventType, FailureEventType, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(envelope.Producer)
            || string.Equals(envelope.Producer, InstitutionCreationService.Producer, StringComparison.Ordinal)
            || !supportedType)
        {
            throw new InvalidOperationException("The institution topic contained an unexpected producer or event type.");
        }
    }

    private static void ValidateAcknowledgement(
        IntegrationEventEnvelope envelope,
        Acknowledgement acknowledgement)
    {
        if (string.IsNullOrWhiteSpace(acknowledgement.InstitutionId)
            || !string.Equals(acknowledgement.InstitutionId, envelope.InstitutionId, StringComparison.Ordinal)
            || !string.Equals(acknowledgement.ServiceId, envelope.Producer, StringComparison.Ordinal)
            || acknowledgement.ProvisioningPlanVersion < 1
            || string.IsNullOrWhiteSpace(acknowledgement.SourceEventId)
            || (acknowledgement.IsFailure && string.IsNullOrWhiteSpace(acknowledgement.FailureCode)))
        {
            throw new InvalidOperationException("The acknowledgement payload does not match the trusted envelope.");
        }
    }

    private sealed record Acknowledgement(
        string InstitutionId,
        string ServiceId,
        int ProvisioningPlanVersion,
        string SourceEventId,
        string? FailureCode)
    {
        public bool IsFailure => FailureCode is not null;
    }
}
