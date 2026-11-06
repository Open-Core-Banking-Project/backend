using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
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
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Institutions;

/// <summary>
/// Authors the <c>institution.currencies</c> area through the approved configuration lifecycle. The
/// stored content is the canonical form of the validated set; only an approved, exact-hash version can
/// be published, and the publisher can never be the maker. Decision: P1-E04-D17.
/// </summary>
public sealed class InstitutionCurrencyConfigurationService : IInstitutionCurrencyConfigurationService
{
    private const string Area = InstitutionCurrencySet.Area;
    private const string Schema = InstitutionCurrencySet.Schema;
    private const string Scope = "institution";
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

    public InstitutionCurrencyConfigurationService(
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

    public async Task<CurrencyDraftResult> CreateDraftAsync(
        CreateCurrencyDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        string versionId = ValidateIdentifier(command.VersionId, nameof(command.VersionId));
        string rawJson = BuildJson(command.BaseCurrency, command.SupportedCurrencies);
        InstitutionCurrencySet parsed;
        try
        {
            parsed = InstitutionCurrencySet.Parse(rawJson);
        }
        catch (ArgumentException exception)
        {
            throw Reject("currency_configuration_invalid", exception.Message);
        }

        string canonicalJson = BuildJson(parsed.BaseCurrency, parsed.SupportedCurrencies);
        ConfigurationContent content = ConfigurationContent.Create(canonicalJson);
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|', versionId, content.Hash));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "institution.currencies.draft.create", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => CreateDraftCoreAsync(command.InstitutionId, command.ActorId, versionId, content, token),
            cancellationToken).ConfigureAwait(false);
        return DecodeDraft(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<CurrencySimulationResult> SimulateAsync(
        SimulateCurrencyVersionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        string versionId = ValidateIdentifier(command.VersionId, nameof(command.VersionId));
        string simulationId = ValidateIdentifier(command.SimulationId, nameof(command.SimulationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ContentHash);
        string contentHash = command.ContentHash.Trim().ToLowerInvariant();
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|', versionId, simulationId, contentHash));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "institution.currencies.simulate", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => SimulateCoreAsync(command.InstitutionId, versionId, simulationId, contentHash, token),
            cancellationToken).ConfigureAwait(false);
        return DecodeSimulation(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<CurrencySubmissionResult> SubmitForApprovalAsync(
        SubmitCurrencyVersionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        string requestId = ValidateIdentifier(command.RequestId, nameof(command.RequestId));
        ValidateIdentifier(command.SourceRequestKey, nameof(command.SourceRequestKey));
        string versionId = ValidateIdentifier(command.VersionId, nameof(command.VersionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ContentHash);
        string contentHash = command.ContentHash.Trim().ToLowerInvariant();
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|', requestId, versionId, contentHash));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "institution.currencies.submit", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => SubmitCoreAsync(command, requestId, versionId, contentHash, token),
            cancellationToken).ConfigureAwait(false);
        return DecodeSubmission(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<CurrencyPublicationResult> PublishAsync(
        PublishCurrencyVersionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        string publicationId = ValidateIdentifier(command.PublicationId, nameof(command.PublicationId));
        string versionId = ValidateIdentifier(command.VersionId, nameof(command.VersionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ContentHash);
        string contentHash = command.ContentHash.Trim().ToLowerInvariant();
        ConfigurationEffectivePeriod.Create(command.EffectiveFrom, command.EffectiveTo);
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            publicationId, versionId, contentHash,
            command.EffectiveFrom.ToString("O", CultureInfo.InvariantCulture),
            command.EffectiveTo?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "institution.currencies.publish", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => PublishCoreAsync(command, publicationId, versionId, contentHash, token),
            cancellationToken).ConfigureAwait(false);
        return DecodePublication(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    private async Task<string?> CreateDraftCoreAsync(
        InstitutionId institutionId,
        ActorId actorId,
        string versionId,
        ConfigurationContent content,
        CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(institutionId, cancellationToken).ConfigureAwait(false);
        string setId = SetId(institutionId);
        ConfigurationSetRecord? set = await _context.ConfigurationSets
            .SingleOrDefaultAsync(record => record.ConfigurationSetId == setId, cancellationToken).ConfigureAwait(false);
        if (set is null)
        {
            set = new ConfigurationSetRecord
            {
                InstitutionId = institutionId.Value,
                ConfigurationSetId = setId,
                Area = Area,
                Scope = Scope,
                Version = 0,
                CreatedAt = _timeProvider.GetUtcNow(),
            };
            _context.ConfigurationSets.Add(set);
        }

        bool duplicate = await _context.ConfigurationVersions
            .AnyAsync(record => record.VersionId == versionId, cancellationToken).ConfigureAwait(false);
        if (duplicate)
        {
            throw Reject("currency_version_conflict", "The configuration version identifier is already used in this institution.");
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
            Schema = Schema,
            ContentJson = content.CanonicalJson,
            ContentHash = content.Hash,
            Status = DraftStatus,
            CreatedByActorId = actorId.Value,
            Version = 1,
            CreatedAt = now,
        });
        await SaveAsync("currency_draft_conflict", "The currency configuration changed while a draft was being created.", cancellationToken).ConfigureAwait(false);
        return EncodeDraft(institutionId.Value, versionId, content.Hash, DraftStatus, now);
    }

    private async Task<string?> SimulateCoreAsync(
        InstitutionId institutionId,
        string versionId,
        string simulationId,
        string contentHash,
        CancellationToken cancellationToken)
    {
        ConfigurationVersionRecord record = await LoadVersionAsync(institutionId, versionId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(record.Status, DraftStatus, StringComparison.Ordinal))
        {
            throw Reject("currency_version_not_simulatable", "Only a draft currency configuration can be simulated.");
        }

        if (!string.Equals(record.ContentHash, contentHash, StringComparison.Ordinal))
        {
            throw Reject("currency_simulation_stale", "The simulation hash does not match the stored currency configuration.");
        }

        ConfigurationContent content = ConfigurationContent.Create(record.ContentJson);
        try
        {
            InstitutionCurrencySet.Parse(record.ContentJson);
        }
        catch (ArgumentException exception)
        {
            throw Reject("currency_configuration_invalid", exception.Message);
        }

        // The validation itself is the simulation: a declarative currency set has no runtime behaviour
        // to exercise, so the evidence is the exact validated content and its hashes.
        DateTimeOffset now = _timeProvider.GetUtcNow();
        _context.ConfigurationSimulations.Add(new ConfigurationSimulationRecord
        {
            InstitutionId = institutionId.Value,
            SimulationId = simulationId,
            VersionId = record.VersionId,
            DatasetVersion = content.Hash,
            Engine = Schema,
            ContentHash = record.ContentHash,
            InputHash = content.Hash,
            OutputHash = content.Hash,
            Outcome = "passed",
            SimulatedAt = now,
        });
        await SaveAsync("currency_simulation_conflict", "The currency simulation could not be recorded.", cancellationToken).ConfigureAwait(false);
        return EncodeSimulation(institutionId.Value, versionId, record.ContentHash, content.Hash, content.Hash, "passed", now);
    }

    private async Task<string?> SubmitCoreAsync(
        SubmitCurrencyVersionCommand command,
        string requestId,
        string versionId,
        string contentHash,
        CancellationToken cancellationToken)
    {
        ConfigurationVersionRecord record = await LoadVersionAsync(command.InstitutionId, versionId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(record.Status, DraftStatus, StringComparison.Ordinal))
        {
            throw Reject("currency_version_not_submittable", "Only a draft currency configuration can be submitted.");
        }

        if (!string.Equals(record.ContentHash, contentHash, StringComparison.Ordinal))
        {
            throw Reject("currency_submission_stale", "The submission hash does not match the stored currency configuration.");
        }

        bool passedExactSimulation = await _context.ConfigurationSimulations.AnyAsync(
            record2 => record2.VersionId == record.VersionId
                && record2.ContentHash == record.ContentHash
                && record2.Outcome == "passed",
            cancellationToken).ConfigureAwait(false);
        if (!passedExactSimulation)
        {
            throw Reject("currency_simulation_required", "The exact currency configuration must pass validation before approval.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string eventId = Guid.NewGuid().ToString("N");
        ApprovalRequestedV1 payload = new(
            record.InstitutionId,
            requestId,
            command.SourceRequestKey,
            InstitutionConfigurationApprovalV1.WorkflowKey,
            InstitutionConfigurationApprovalV1.WorkflowVersionId,
            InstitutionConfigurationApprovalV1.WorkflowDefinitionHash,
            InstitutionConfigurationApprovalV1.SourceService,
            InstitutionConfigurationApprovalV1.SubjectType,
            record.VersionId,
            record.VersionNumber.ToString(CultureInfo.InvariantCulture),
            record.ContentHash,
            command.ActorId.Value,
            InstitutionConfigurationApprovalV1.RequiredPermission,
            command.CorrelationId,
            now);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = ApprovalRequestedEventType,
            SchemaVersion = ApprovalSchemaVersion,
            OccurredAt = now,
            InstitutionId = record.InstitutionId,
            CorrelationId = command.CorrelationId,
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
            CorrelationId = command.CorrelationId,
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
        await SaveAsync("currency_submission_conflict", "The currency configuration changed while approval was being submitted.", cancellationToken).ConfigureAwait(false);
        return EncodeSubmission(record.InstitutionId, requestId, record.VersionId, record.ContentHash, eventId, SubmittedStatus, now);
    }

    private async Task<string?> PublishCoreAsync(
        PublishCurrencyVersionCommand command,
        string publicationId,
        string versionId,
        string contentHash,
        CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(command.InstitutionId, cancellationToken).ConfigureAwait(false);
        ConfigurationVersionRecord record = await LoadVersionAsync(command.InstitutionId, versionId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(record.Schema, Schema, StringComparison.Ordinal)
            || !string.Equals(record.Status, ApprovedStatus, StringComparison.Ordinal))
        {
            throw Reject("currency_version_not_publishable", "Only an approved currency configuration can be published.");
        }

        if (!string.Equals(record.ContentHash, contentHash, StringComparison.Ordinal))
        {
            throw Reject("currency_publication_stale", "The publication hash does not match the approved currency configuration.");
        }

        if (string.IsNullOrWhiteSpace(record.ApprovalReference) || string.IsNullOrWhiteSpace(record.ApprovedByActorId))
        {
            throw Reject("currency_approval_evidence_missing", "The approved version has no trusted Workflow decision evidence.");
        }

        if (string.Equals(record.CreatedByActorId, command.ActorId.Value, StringComparison.Ordinal))
        {
            throw Reject("currency_self_publication", "The currency configuration maker cannot publish their own version.");
        }

        ConfigurationContent storedContent = ConfigurationContent.Create(record.ContentJson);
        if (!string.Equals(storedContent.Hash, record.ContentHash, StringComparison.Ordinal))
        {
            throw Reject("currency_content_integrity_failed", "The stored currency configuration does not match its recorded hash.");
        }

        try
        {
            InstitutionCurrencySet.Parse(record.ContentJson);
        }
        catch (ArgumentException exception)
        {
            throw Reject("currency_configuration_invalid", exception.Message);
        }

        ConfigurationEffectivePeriod proposed = ConfigurationEffectivePeriod.Create(command.EffectiveFrom, command.EffectiveTo);
        var publicationPeriods = await _context.ConfigurationPublications
            .Where(record2 => record2.ConfigurationSetId == record.ConfigurationSetId)
            .Select(record2 => new { record2.VersionId, record2.EffectiveFrom, record2.EffectiveTo })
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        PublishedConfigurationVersion[] existing = publicationPeriods
            .Select(record2 => new PublishedConfigurationVersion(
                record2.VersionId,
                ConfigurationEffectivePeriod.Create(record2.EffectiveFrom, record2.EffectiveTo)))
            .ToArray();
        try
        {
            ConfigurationPublicationPolicy.EnsureNoOverlap(existing, proposed);
        }
        catch (InvalidOperationException)
        {
            throw Reject("currency_publication_period_overlaps", "Another published currency configuration overlaps the requested period.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        _context.ConfigurationPublications.Add(new ConfigurationPublicationRecord
        {
            InstitutionId = record.InstitutionId,
            PublicationId = publicationId,
            ConfigurationSetId = record.ConfigurationSetId,
            VersionId = record.VersionId,
            ContentHash = record.ContentHash,
            PublishedByActorId = command.ActorId.Value,
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
        await SaveAsync("currency_publication_conflict", "The currency configuration changed while it was being published.", cancellationToken).ConfigureAwait(false);
        return EncodePublication(
            record.InstitutionId,
            publicationId,
            record.VersionId,
            record.ContentHash,
            proposed.EffectiveFrom,
            proposed.EffectiveTo,
            now);
    }

    private async Task<ConfigurationVersionRecord> LoadVersionAsync(
        InstitutionId institutionId,
        string versionId,
        CancellationToken cancellationToken)
    {
        ConfigurationVersionRecord? record = await _context.ConfigurationVersions
            .SingleOrDefaultAsync(candidate => candidate.VersionId == versionId
                && candidate.InstitutionId == institutionId.Value
                && candidate.Schema == Schema,
                cancellationToken).ConfigureAwait(false);
        return record ?? throw Reject("currency_version_not_found", "The currency configuration version is not available in this institution.");
    }

    private async Task EnsureInstitutionActiveAsync(InstitutionId institutionId, CancellationToken cancellationToken)
    {
        InstitutionRecord? institution = await _context.Institutions
            .SingleOrDefaultAsync(record => record.InstitutionId == institutionId.Value, cancellationToken).ConfigureAwait(false);
        if (institution is null || !string.Equals(institution.Status, "active", StringComparison.Ordinal))
        {
            throw Reject("institution_inactive", "Currency configuration requires an active institution.");
        }
    }

    private async Task SaveAsync(string code, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            throw new InstitutionCurrencyConfigurationRejectedException(code, message, exception);
        }
    }

    private void ValidateTarget(InstitutionId institutionId, string idempotencyKey, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (_tenantContext.CurrentInstitution != institutionId)
        {
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the configuration target.");
        }
    }

    private static string SetId(InstitutionId institutionId) => string.Concat("institution-currencies:", institutionId.Value);

    private static string ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string trimmed = value.Trim();
        if (trimmed.Length > 128)
        {
            throw Reject("currency_identifier_invalid", "A configuration identifier may contain at most 128 characters.");
        }

        if (trimmed.Contains('|'))
        {
            throw Reject("currency_identifier_invalid", "A configuration identifier may not contain the separator character.");
        }

        return trimmed;
    }

    private static string BuildJson(string baseCurrency, IReadOnlyCollection<string> supportedCurrencies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCurrency);
        ArgumentNullException.ThrowIfNull(supportedCurrencies);
        return JsonSerializer.Serialize(new CurrencyContentPayload(
            baseCurrency.Trim(),
            supportedCurrencies.Select(code => code?.Trim() ?? string.Empty).ToArray()));
    }

    private sealed record CurrencyContentPayload(string baseCurrency, string[] supportedCurrencies);

    private static InstitutionCurrencyConfigurationRejectedException Reject(string code, string message) => new(code, message);

    private static string EncodeDraft(string institutionId, string versionId, string contentHash, string status, DateTimeOffset changedAt) =>
        string.Join('|', institutionId, versionId, contentHash, status, changedAt.ToString("O", CultureInfo.InvariantCulture));

    private static string EncodeSimulation(string institutionId, string versionId, string contentHash, string inputHash, string outputHash, string outcome, DateTimeOffset simulatedAt) =>
        string.Join('|', institutionId, versionId, contentHash, inputHash, outputHash, outcome, simulatedAt.ToString("O", CultureInfo.InvariantCulture));

    private static string EncodeSubmission(string institutionId, string requestId, string versionId, string contentHash, string eventId, string status, DateTimeOffset submittedAt) =>
        string.Join('|', institutionId, requestId, versionId, contentHash, eventId, status, submittedAt.ToString("O", CultureInfo.InvariantCulture));

    private static string EncodePublication(string institutionId, string publicationId, string versionId, string contentHash, DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo, DateTimeOffset publishedAt) =>
        string.Join('|', institutionId, publicationId, versionId, contentHash,
            effectiveFrom.ToString("O", CultureInfo.InvariantCulture),
            effectiveTo?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            publishedAt.ToString("O", CultureInfo.InvariantCulture));

    private static CurrencyDraftResult DecodeDraft(IdempotencyOutcome outcome, bool replayed)
    {
        string[] parts = EnsureExecuted(outcome);
        if (parts.Length != 5
            || !DateTimeOffset.TryParse(parts[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset changedAt))
        {
            throw new InvalidOperationException("The stored currency draft evidence is not readable.");
        }

        return new CurrencyDraftResult(parts[0], parts[1], parts[2], parts[3], changedAt, replayed);
    }

    private static CurrencySimulationResult DecodeSimulation(IdempotencyOutcome outcome, bool replayed)
    {
        string[] parts = EnsureExecuted(outcome);
        if (parts.Length != 7
            || !DateTimeOffset.TryParse(parts[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset simulatedAt))
        {
            throw new InvalidOperationException("The stored currency simulation evidence is not readable.");
        }

        return new CurrencySimulationResult(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], simulatedAt, replayed);
    }

    private static CurrencySubmissionResult DecodeSubmission(IdempotencyOutcome outcome, bool replayed)
    {
        string[] parts = EnsureExecuted(outcome);
        if (parts.Length != 7
            || !DateTimeOffset.TryParse(parts[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset submittedAt))
        {
            throw new InvalidOperationException("The stored currency submission evidence is not readable.");
        }

        return new CurrencySubmissionResult(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], submittedAt, replayed);
    }

    private static CurrencyPublicationResult DecodePublication(IdempotencyOutcome outcome, bool replayed)
    {
        string[] parts = EnsureExecuted(outcome);
        if (parts.Length != 7
            || !DateTimeOffset.TryParse(parts[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset effectiveFrom)
            || !DateTimeOffset.TryParse(parts[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset publishedAt))
        {
            throw new InvalidOperationException("The stored currency publication evidence is not readable.");
        }

        DateTimeOffset? effectiveTo = string.IsNullOrEmpty(parts[5])
            ? null
            : DateTimeOffset.Parse(parts[5], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return new CurrencyPublicationResult(parts[0], parts[1], parts[2], parts[3], effectiveFrom, effectiveTo, publishedAt, replayed);
    }

    private static string[] EnsureExecuted(IdempotencyOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (string.IsNullOrWhiteSpace(outcome.Result))
        {
            throw new InvalidOperationException("The configuration command did not produce an idempotent result.");
        }

        return outcome.Result.Split('|');
    }
}
