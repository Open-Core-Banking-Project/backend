using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using CoreBanking.CoreLedger.Infrastructure.Products;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Institutions;

/// <summary>
/// The shared implementation of the approved institution configuration lifecycle. Only an approved,
/// exact-hash version can be published, the publisher is never the maker, and publication periods
/// never overlap. Area-specific validation is supplied by the caller. Decisions: P1-E04-D07, P1-E04-D17.
/// </summary>
public sealed class InstitutionConfigurationAreaLifecycle : IInstitutionConfigurationAreaLifecycle
{
    private const string Producer = "core-ledger";
    private const string ApprovalEventFamily = "approval";
    private const string ApprovalRequestedEventType = "approval_requested";
    private const int ApprovalSchemaVersion = 1;
    private const string DraftStatus = "draft";
    private const string SubmittedStatus = "submitted";
    private const string ApprovedStatus = "approved";
    private const string PublishedStatus = "published";

    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly ProductAccountMetrics? _metrics;

    public InstitutionConfigurationAreaLifecycle(
        CoreLedgerDbContext context,
        IdempotencyService idempotency,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        ProductAccountMetrics? metrics = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _metrics = metrics;
    }

    public async Task<ConfigurationDraftOutcome> CreateDraftAsync(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        ActorId actorId,
        string idempotencyKey,
        string versionId,
        ConfigurationContent content,
        string draftAuditAction,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(draftAuditAction);
        ValidateTarget(area, institutionId, idempotencyKey, correlationId);
        string identifier = ValidateIdentifier(area, versionId, nameof(versionId));

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|', identifier, content.Hash));
        IdempotencyOutcome outcome = await ExecuteRecordedAsync(
            area,
            IdempotencyKey.Create(institutionId.Value, $"{area.Area}.draft.create", actorId.Value, idempotencyKey),
            fingerprint,
            "draft.create",
            async token =>
            {
                string? result = await CreateDraftCoreAsync(
                    area, institutionId, actorId, identifier, content, draftAuditAction, correlationId, token)
                    .ConfigureAwait(false);
                return result;
            },
            cancellationToken).ConfigureAwait(false);
        return Decode<ConfigurationDraftOutcome>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<ConfigurationSimulationOutcome> SimulateAsync(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        ActorId actorId,
        string idempotencyKey,
        string versionId,
        string simulationId,
        string contentHash,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(area);
        ValidateTarget(area, institutionId, idempotencyKey, correlationId);
        string identifier = ValidateIdentifier(area, versionId, nameof(versionId));
        string simulation = ValidateIdentifier(area, simulationId, nameof(simulationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        string hash = contentHash.Trim().ToLowerInvariant();

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|', identifier, simulation, hash));
        IdempotencyOutcome outcome = await ExecuteRecordedAsync(
            area,
            IdempotencyKey.Create(institutionId.Value, $"{area.Area}.simulate", actorId.Value, idempotencyKey),
            fingerprint,
            "simulate",
            token => SimulateCoreAsync(area, institutionId, actorId, identifier, simulation, hash, correlationId, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<ConfigurationSimulationOutcome>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<ConfigurationSubmissionOutcome> SubmitAsync(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        ActorId actorId,
        string idempotencyKey,
        string requestId,
        string sourceRequestKey,
        string versionId,
        string contentHash,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(area);
        ValidateTarget(area, institutionId, idempotencyKey, correlationId);
        string request = ValidateIdentifier(area, requestId, nameof(requestId));
        ValidateIdentifier(area, sourceRequestKey, nameof(sourceRequestKey));
        string identifier = ValidateIdentifier(area, versionId, nameof(versionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        string hash = contentHash.Trim().ToLowerInvariant();

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|', request, identifier, hash));
        IdempotencyOutcome outcome = await ExecuteRecordedAsync(
            area,
            IdempotencyKey.Create(institutionId.Value, $"{area.Area}.submit", actorId.Value, idempotencyKey),
            fingerprint,
            "submit",
            token => SubmitCoreAsync(area, institutionId, actorId, request, sourceRequestKey, identifier, hash, correlationId, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<ConfigurationSubmissionOutcome>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<ConfigurationPublicationOutcome> PublishAsync(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        ActorId actorId,
        string idempotencyKey,
        string publicationId,
        string versionId,
        string contentHash,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        string correlationId,
        CancellationToken cancellationToken,
        Func<string, DateTimeOffset, CancellationToken, Task>? validateOnPublish = null)
    {
        ArgumentNullException.ThrowIfNull(area);
        ValidateTarget(area, institutionId, idempotencyKey, correlationId);
        string publication = ValidateIdentifier(area, publicationId, nameof(publicationId));
        string identifier = ValidateIdentifier(area, versionId, nameof(versionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        string hash = contentHash.Trim().ToLowerInvariant();

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            publication,
            identifier,
            hash,
            effectiveFrom.ToString("O", CultureInfo.InvariantCulture),
            effectiveTo?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty));
        IdempotencyOutcome outcome = await ExecuteRecordedAsync(
            area,
            IdempotencyKey.Create(institutionId.Value, $"{area.Area}.publish", actorId.Value, idempotencyKey),
            fingerprint,
            "publish",
            token => PublishCoreAsync(
                area, institutionId, actorId, publication, identifier, hash, effectiveFrom, effectiveTo,
                correlationId, validateOnPublish, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<ConfigurationPublicationOutcome>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    private async Task<string?> CreateDraftCoreAsync(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        ActorId actorId,
        string versionId,
        ConfigurationContent content,
        string draftAuditAction,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(area, institutionId, cancellationToken).ConfigureAwait(false);
        string setId = SetId(area, institutionId);
        ConfigurationSetRecord? set = await _context.ConfigurationSets
            .SingleOrDefaultAsync(record => record.ConfigurationSetId == setId, cancellationToken).ConfigureAwait(false);
        if (set is null)
        {
            _context.ConfigurationSets.Add(new ConfigurationSetRecord
            {
                InstitutionId = institutionId.Value,
                ConfigurationSetId = setId,
                Area = area.Area,
                Scope = area.Scope,
                Version = 0,
                CreatedAt = _timeProvider.GetUtcNow(),
            });
        }

        bool duplicate = await _context.ConfigurationVersions
            .AnyAsync(record => record.VersionId == versionId, cancellationToken).ConfigureAwait(false);
        if (duplicate)
        {
            throw Reject(area, "version_conflict", "The configuration version identifier is already used in this institution.");
        }

        int versionNumber = await _context.ConfigurationVersions
            .CountAsync(record => record.ConfigurationSetId == setId, cancellationToken).ConfigureAwait(false) + 1;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        _context.ConfigurationVersions.Add(new ConfigurationVersionRecord
        {
            InstitutionId = institutionId.Value,
            VersionId = versionId,
            ConfigurationSetId = setId,
            VersionNumber = versionNumber,
            Schema = area.Schema,
            ContentJson = content.CanonicalJson,
            ContentHash = content.Hash,
            Status = DraftStatus,
            CreatedByActorId = actorId.Value,
            Version = 1,
            CreatedAt = now,
        });
        AddAudit(institutionId, actorId, "institution.configuration.create", draftAuditAction, versionId, content.Hash, correlationId, now);
        await SaveAsync(area, "draft_conflict", "The configuration changed while a draft was being created.", cancellationToken).ConfigureAwait(false);
        return Json(content.Hash, institutionId.Value, versionId, DraftStatus, now);
    }

    private async Task<string?> SimulateCoreAsync(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        ActorId actorId,
        string versionId,
        string simulationId,
        string contentHash,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ConfigurationVersionRecord record = await LoadVersionAsync(area, institutionId, versionId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(record.Status, DraftStatus, StringComparison.Ordinal))
        {
            throw Reject(area, "version_not_simulatable", "Only a draft configuration version can be simulated.");
        }

        if (!string.Equals(record.ContentHash, contentHash, StringComparison.Ordinal))
        {
            throw Reject(area, "simulation_stale", "The simulation hash does not match the stored configuration.");
        }

        ConfigurationContent content = ValidateContent(area, record.ContentJson);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        _context.ConfigurationSimulations.Add(new ConfigurationSimulationRecord
        {
            InstitutionId = institutionId.Value,
            SimulationId = simulationId,
            VersionId = record.VersionId,
            DatasetVersion = content.Hash,
            Engine = area.Schema,
            ContentHash = record.ContentHash,
            InputHash = content.Hash,
            OutputHash = content.Hash,
            Outcome = "passed",
            SimulatedAt = now,
        });
        AddAudit(institutionId, actorId, "institution.configuration.simulate", $"{area.Area}.simulation.record", simulationId, record.ContentHash, correlationId, now);
        await SaveAsync(area, "simulation_conflict", "The configuration simulation could not be recorded.", cancellationToken).ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Serialize(new ConfigurationSimulationOutcome(
            institutionId.Value, versionId, record.ContentHash, content.Hash, content.Hash, "passed", now));
    }

    private async Task<string?> SubmitCoreAsync(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        ActorId actorId,
        string requestId,
        string sourceRequestKey,
        string versionId,
        string contentHash,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ConfigurationVersionRecord record = await LoadVersionAsync(area, institutionId, versionId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(record.Status, DraftStatus, StringComparison.Ordinal))
        {
            throw Reject(area, "version_not_submittable", "Only a draft configuration version can be submitted.");
        }

        if (!string.Equals(record.ContentHash, contentHash, StringComparison.Ordinal))
        {
            throw Reject(area, "submission_stale", "The submission hash does not match the stored configuration.");
        }

        bool passedExactSimulation = await _context.ConfigurationSimulations.AnyAsync(
            candidate => candidate.VersionId == record.VersionId
                && candidate.ContentHash == record.ContentHash
                && candidate.Outcome == "passed",
            cancellationToken).ConfigureAwait(false);
        if (!passedExactSimulation)
        {
            throw Reject(area, "simulation_required", "The exact configuration must pass validation before approval.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string eventId = Guid.NewGuid().ToString("N");
        ApprovalRequestedV1 payload = new(
            record.InstitutionId,
            requestId,
            sourceRequestKey,
            InstitutionConfigurationApprovalV1.WorkflowKey,
            InstitutionConfigurationApprovalV1.WorkflowVersionId,
            InstitutionConfigurationApprovalV1.WorkflowDefinitionHash,
            InstitutionConfigurationApprovalV1.SourceService,
            InstitutionConfigurationApprovalV1.SubjectType,
            record.VersionId,
            record.VersionNumber.ToString(CultureInfo.InvariantCulture),
            record.ContentHash,
            actorId.Value,
            InstitutionConfigurationApprovalV1.RequiredPermission,
            correlationId,
            now);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = ApprovalRequestedEventType,
            SchemaVersion = ApprovalSchemaVersion,
            OccurredAt = now,
            InstitutionId = record.InstitutionId,
            CorrelationId = correlationId,
            Producer = Producer,
            BusinessReference = requestId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = record.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = ApprovalRequestedEventType,
            SchemaVersion = ApprovalSchemaVersion,
            Topic = EventContract.TopicFor(Producer, ApprovalEventFamily, ApprovalSchemaVersion),
            BusinessReference = requestId,
            CorrelationId = correlationId,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });
        record.Status = SubmittedStatus;
        record.ApprovalReference = requestId;
        record.Version++;
        AddAudit(institutionId, actorId, "institution.configuration.submit", $"{area.Area}.approval.submit", record.VersionId, requestId, correlationId, now);
        await SaveAsync(area, "submission_conflict", "The configuration changed while approval was being submitted.", cancellationToken).ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Serialize(new ConfigurationSubmissionOutcome(
            record.InstitutionId, requestId, record.VersionId, record.ContentHash, eventId, SubmittedStatus, now));
    }

    private async Task<string?> PublishCoreAsync(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        ActorId actorId,
        string publicationId,
        string versionId,
        string contentHash,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        string correlationId,
        Func<string, DateTimeOffset, CancellationToken, Task>? validateOnPublish,
        CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(area, institutionId, cancellationToken).ConfigureAwait(false);
        ConfigurationVersionRecord record = await LoadVersionAsync(area, institutionId, versionId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(record.Status, ApprovedStatus, StringComparison.Ordinal))
        {
            throw Reject(area, "version_not_publishable", "Only an approved configuration version can be published.");
        }

        if (!string.Equals(record.ContentHash, contentHash, StringComparison.Ordinal))
        {
            throw Reject(area, "publication_stale", "The publication hash does not match the approved configuration.");
        }

        if (string.IsNullOrWhiteSpace(record.ApprovalReference) || string.IsNullOrWhiteSpace(record.ApprovedByActorId))
        {
            throw Reject(area, "approval_evidence_missing", "The approved version has no trusted Workflow decision evidence.");
        }

        if (string.Equals(record.CreatedByActorId, actorId.Value, StringComparison.Ordinal))
        {
            throw Reject(area, "self_publication", "The configuration maker cannot publish their own version.");
        }

        ConfigurationContent storedContent = ConfigurationContent.Create(record.ContentJson);
        if (!string.Equals(storedContent.Hash, record.ContentHash, StringComparison.Ordinal))
        {
            throw Reject(area, "content_integrity_failed", "The stored configuration does not match its recorded hash.");
        }

        ValidateContent(area, record.ContentJson);
        if (validateOnPublish is not null)
        {
            await validateOnPublish(record.ContentJson, effectiveFrom, cancellationToken).ConfigureAwait(false);
        }

        ConfigurationEffectivePeriod proposed = ConfigurationEffectivePeriod.Create(effectiveFrom, effectiveTo);
        var publicationPeriods = await _context.ConfigurationPublications
            .Where(candidate => candidate.ConfigurationSetId == record.ConfigurationSetId)
            .Select(candidate => new { candidate.VersionId, candidate.EffectiveFrom, candidate.EffectiveTo })
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        PublishedConfigurationVersion[] existing = publicationPeriods
            .Select(candidate => new PublishedConfigurationVersion(
                candidate.VersionId,
                ConfigurationEffectivePeriod.Create(candidate.EffectiveFrom, candidate.EffectiveTo)))
            .ToArray();
        try
        {
            ConfigurationPublicationPolicy.EnsureNoOverlap(existing, proposed);
        }
        catch (InvalidOperationException)
        {
            throw Reject(area, "publication_period_overlaps", "Another published version overlaps the requested period.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        _context.ConfigurationPublications.Add(new ConfigurationPublicationRecord
        {
            InstitutionId = record.InstitutionId,
            PublicationId = publicationId,
            ConfigurationSetId = record.ConfigurationSetId,
            VersionId = record.VersionId,
            ContentHash = record.ContentHash,
            PublishedByActorId = actorId.Value,
            ApprovalReference = record.ApprovalReference,
            EffectiveFrom = proposed.EffectiveFrom,
            EffectiveTo = proposed.EffectiveTo,
            PublishedAt = now,
        });
        record.Status = PublishedStatus;
        record.EffectiveFrom = proposed.EffectiveFrom;
        record.EffectiveTo = proposed.EffectiveTo;
        record.PublishedAt = now;
        record.Version++;
        AddAudit(institutionId, actorId, "institution.configuration.publish", $"{area.Area}.publish", record.VersionId, publicationId, correlationId, now);
        await SaveAsync(area, "publication_conflict", "The configuration changed while it was being published.", cancellationToken).ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Serialize(new ConfigurationPublicationOutcome(
            record.InstitutionId, publicationId, record.VersionId, record.ContentHash,
            proposed.EffectiveFrom, proposed.EffectiveTo, now));
    }

    private static ConfigurationContent ValidateContent(InstitutionConfigurationArea area, string contentJson)
    {
        try
        {
            return area.Validate(contentJson);
        }
        catch (ArgumentException exception)
        {
            throw Reject(area, "configuration_invalid", exception.Message);
        }
    }

    private async Task<ConfigurationVersionRecord> LoadVersionAsync(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        string versionId,
        CancellationToken cancellationToken)
    {
        ConfigurationVersionRecord? record = await _context.ConfigurationVersions
            .SingleOrDefaultAsync(candidate => candidate.VersionId == versionId
                && candidate.InstitutionId == institutionId.Value
                && candidate.Schema == area.Schema,
                cancellationToken).ConfigureAwait(false);
        return record ?? throw Reject(area, "version_not_found", "The configuration version is not available in this institution.");
    }

    private async Task EnsureInstitutionActiveAsync(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        CancellationToken cancellationToken)
    {
        InstitutionRecord? institution = await _context.Institutions
            .SingleOrDefaultAsync(record => record.InstitutionId == institutionId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (institution is null || !string.Equals(institution.Status, "active", StringComparison.Ordinal))
        {
            throw Reject(area, "institution_inactive", "Configuration requires an active institution.");
        }
    }

    private async Task SaveAsync(
        InstitutionConfigurationArea area,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            throw new InstitutionConfigurationRejectedException(Code(area, code), message, exception);
        }
    }

    private void ValidateTarget(
        InstitutionConfigurationArea area,
        InstitutionId institutionId,
        string idempotencyKey,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (_tenantContext.CurrentInstitution != institutionId)
        {
            throw Reject(area, "cross_tenant_access_denied", "The authenticated institution does not match the configuration target.");
        }
    }

    private async Task<IdempotencyOutcome> ExecuteRecordedAsync(
        InstitutionConfigurationArea area,
        IdempotencyKey key,
        string fingerprint,
        string command,
        Func<CancellationToken, Task<string?>> core,
        CancellationToken cancellationToken)
    {
        try
        {
            IdempotencyOutcome outcome = await _idempotency
                .ExecuteAsync(key, fingerprint, core, cancellationToken)
                .ConfigureAwait(false);
            _metrics?.RecordConfigurationCommand(
                area.Area,
                command,
                outcome.Kind == IdempotencyOutcomeKind.Replayed ? "replayed" : "succeeded");
            return outcome;
        }
        catch (InstitutionConfigurationRejectedException)
        {
            _metrics?.RecordConfigurationCommand(area.Area, command, "rejected");
            throw;
        }
    }

    private void AddAudit(
        InstitutionId institutionId,
        ActorId actorId,
        string permission,
        string action,
        string resourceId,
        string afterReference,
        string correlationId,
        DateTimeOffset occurredAt)
    {
        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = institutionId.Value,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = actorId.Value,
            ActorType = "human",
            Permission = permission,
            Action = action,
            ResourceType = "institution.configuration.version",
            ResourceId = resourceId,
            Outcome = "succeeded",
            Reason = string.Empty,
            CorrelationId = correlationId,
            AfterReference = afterReference,
            OccurredAt = occurredAt,
        });
    }

    private static string SetId(InstitutionConfigurationArea area, InstitutionId institutionId) =>
        string.Concat(area.SetIdPrefix, ":", institutionId.Value);

    private static string ValidateIdentifier(InstitutionConfigurationArea area, string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string trimmed = value.Trim();
        if (trimmed.Length > 128 || trimmed.Contains('|'))
        {
            throw Reject(area, "identifier_invalid", "A configuration identifier may contain at most 128 characters and no separator.");
        }

        return trimmed;
    }

    private static string Code(InstitutionConfigurationArea area, string suffix) =>
        $"{area.CodePrefix}_{suffix}";

    private static InstitutionConfigurationRejectedException Reject(
        InstitutionConfigurationArea area,
        string suffix,
        string message) => new(Code(area, suffix), message);

    private static string Json(string contentHash, string institutionId, string versionId, string status, DateTimeOffset changedAt) =>
        System.Text.Json.JsonSerializer.Serialize(new ConfigurationDraftOutcome(institutionId, versionId, contentHash, status, changedAt));

    private static T Decode<T>(IdempotencyOutcome outcome, bool replayed) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (string.IsNullOrWhiteSpace(outcome.Result))
        {
            throw new InvalidOperationException("The configuration command did not produce an idempotent result.");
        }

        T? decoded = System.Text.Json.JsonSerializer.Deserialize<T>(outcome.Result);
        if (decoded is null)
        {
            throw new InvalidOperationException("The stored configuration evidence is not readable.");
        }

        return decoded;
    }
}
