using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using CoreBanking.CoreLedger.Infrastructure.Products;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Authors the <c>institution.account-opening-policy</c> area through the approved lifecycle: draft,
/// exact-content simulation, Workflow approval and effective-dated publication. Only an approved,
/// exact-hash version can be published and the publisher is never the maker. Decision: P1-E04-D07.
/// </summary>
public sealed class AccountOpeningPolicyConfigurationService : IAccountOpeningPolicyConfigurationService
{
    private const string Area = AccountOpeningPolicy.Area;
    private const string Schema = AccountOpeningPolicy.Schema;
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
    private readonly ProductAccountMetrics? _metrics;

    public AccountOpeningPolicyConfigurationService(
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

    public async Task<AccountOpeningPolicyDraftResult> CreateDraftAsync(
        CreateAccountOpeningPolicyDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        string versionId = ValidateIdentifier(command.VersionId, nameof(command.VersionId));
        AccountOpeningPolicy policy;
        try
        {
            policy = AccountOpeningPolicy.Create(
                command.LimitKey,
                command.MaximumOpenAccounts,
                command.OverrideAdditionalAccounts);
        }
        catch (ArgumentException exception)
        {
            throw Reject("account_opening_policy_invalid", exception.Message);
        }

        ConfigurationContent content = policy.ToConfigurationContent();
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|', versionId, content.Hash));
        IdempotencyOutcome outcome = await ExecuteRecordedAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "institution.account-opening-policy.draft.create", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            "draft.create",
            token => CreateDraftCoreAsync(command, versionId, content, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<AccountOpeningPolicyDraftResult>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountOpeningPolicySimulationResult> SimulateAsync(
        SimulateAccountOpeningPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        string versionId = ValidateIdentifier(command.VersionId, nameof(command.VersionId));
        string simulationId = ValidateIdentifier(command.SimulationId, nameof(command.SimulationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ContentHash);
        string contentHash = command.ContentHash.Trim().ToLowerInvariant();
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|', versionId, simulationId, contentHash));
        IdempotencyOutcome outcome = await ExecuteRecordedAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "institution.account-opening-policy.simulate", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            "simulate",
            token => SimulateCoreAsync(command, versionId, simulationId, contentHash, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<AccountOpeningPolicySimulationResult>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountOpeningPolicySubmissionResult> SubmitForApprovalAsync(
        SubmitAccountOpeningPolicyCommand command,
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
        IdempotencyOutcome outcome = await ExecuteRecordedAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "institution.account-opening-policy.submit", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            "submit",
            token => SubmitCoreAsync(command, requestId, versionId, contentHash, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<AccountOpeningPolicySubmissionResult>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountOpeningPolicyPublicationResult> PublishAsync(
        PublishAccountOpeningPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        string publicationId = ValidateIdentifier(command.PublicationId, nameof(command.PublicationId));
        string versionId = ValidateIdentifier(command.VersionId, nameof(command.VersionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ContentHash);
        string contentHash = command.ContentHash.Trim().ToLowerInvariant();
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            publicationId, versionId, contentHash,
            command.EffectiveFrom.ToString("O", CultureInfo.InvariantCulture),
            command.EffectiveTo?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty));
        IdempotencyOutcome outcome = await ExecuteRecordedAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "institution.account-opening-policy.publish", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            "publish",
            token => PublishCoreAsync(command, publicationId, versionId, contentHash, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<AccountOpeningPolicyPublicationResult>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    private async Task<string?> CreateDraftCoreAsync(
        CreateAccountOpeningPolicyDraftCommand command,
        string versionId,
        ConfigurationContent content,
        CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(command.InstitutionId, cancellationToken).ConfigureAwait(false);
        string setId = SetId(command.InstitutionId);
        ConfigurationSetRecord? set = await _context.ConfigurationSets
            .SingleOrDefaultAsync(record => record.ConfigurationSetId == setId, cancellationToken).ConfigureAwait(false);
        if (set is null)
        {
            _context.ConfigurationSets.Add(new ConfigurationSetRecord
            {
                InstitutionId = command.InstitutionId.Value,
                ConfigurationSetId = setId,
                Area = Area,
                Scope = Scope,
                Version = 0,
                CreatedAt = _timeProvider.GetUtcNow(),
            });
        }

        bool duplicate = await _context.ConfigurationVersions
            .AnyAsync(record => record.VersionId == versionId, cancellationToken).ConfigureAwait(false);
        if (duplicate)
        {
            throw Reject("account_opening_policy_version_conflict", "The configuration version identifier is already used in this institution.");
        }

        int versionNumber = await _context.ConfigurationVersions
            .CountAsync(record => record.ConfigurationSetId == setId, cancellationToken).ConfigureAwait(false) + 1;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        _context.ConfigurationVersions.Add(new ConfigurationVersionRecord
        {
            InstitutionId = command.InstitutionId.Value,
            VersionId = versionId,
            ConfigurationSetId = setId,
            VersionNumber = versionNumber,
            Schema = Schema,
            ContentJson = content.CanonicalJson,
            ContentHash = content.Hash,
            Status = DraftStatus,
            CreatedByActorId = command.ActorId.Value,
            Version = 1,
            CreatedAt = now,
        });
        AddAudit(command.InstitutionId, command.ActorId, "institution.configuration.create", "institution.account_opening_policy.draft.create", versionId, content.Hash, command.CorrelationId, now);
        await SaveAsync("account_opening_policy_draft_conflict", "The account-opening policy changed while a draft was being created.", cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new AccountOpeningPolicyDraftResult(
            command.InstitutionId.Value, versionId, content.Hash, DraftStatus, now, false));
    }

    private async Task<string?> SimulateCoreAsync(
        SimulateAccountOpeningPolicyCommand command,
        string versionId,
        string simulationId,
        string contentHash,
        CancellationToken cancellationToken)
    {
        ConfigurationVersionRecord record = await LoadVersionAsync(command.InstitutionId, versionId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(record.Status, DraftStatus, StringComparison.Ordinal))
        {
            throw Reject("account_opening_policy_version_not_simulatable", "Only a draft account-opening policy can be simulated.");
        }

        if (!string.Equals(record.ContentHash, contentHash, StringComparison.Ordinal))
        {
            throw Reject("account_opening_policy_simulation_stale", "The simulation hash does not match the stored account-opening policy.");
        }

        ConfigurationContent content = ConfigurationContent.Create(record.ContentJson);
        try
        {
            AccountOpeningPolicy.Parse(record.ContentJson);
        }
        catch (ArgumentException exception)
        {
            throw Reject("account_opening_policy_invalid", exception.Message);
        }

        // A declarative ceiling has no runtime behaviour to exercise, so the evidence is the exact
        // validated content and its hashes.
        DateTimeOffset now = _timeProvider.GetUtcNow();
        _context.ConfigurationSimulations.Add(new ConfigurationSimulationRecord
        {
            InstitutionId = command.InstitutionId.Value,
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
        AddAudit(command.InstitutionId, command.ActorId, "institution.configuration.simulate", "institution.account_opening_policy.simulation.record", simulationId, record.ContentHash, command.CorrelationId, now);
        await SaveAsync("account_opening_policy_simulation_conflict", "The account-opening policy simulation could not be recorded.", cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new AccountOpeningPolicySimulationResult(
            command.InstitutionId.Value, versionId, record.ContentHash, content.Hash, content.Hash, "passed", now, false));
    }

    private async Task<string?> SubmitCoreAsync(
        SubmitAccountOpeningPolicyCommand command,
        string requestId,
        string versionId,
        string contentHash,
        CancellationToken cancellationToken)
    {
        ConfigurationVersionRecord record = await LoadVersionAsync(command.InstitutionId, versionId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(record.Status, DraftStatus, StringComparison.Ordinal))
        {
            throw Reject("account_opening_policy_version_not_submittable", "Only a draft account-opening policy can be submitted.");
        }

        if (!string.Equals(record.ContentHash, contentHash, StringComparison.Ordinal))
        {
            throw Reject("account_opening_policy_submission_stale", "The submission hash does not match the stored account-opening policy.");
        }

        bool passedExactSimulation = await _context.ConfigurationSimulations.AnyAsync(
            candidate => candidate.VersionId == record.VersionId
                && candidate.ContentHash == record.ContentHash
                && candidate.Outcome == "passed",
            cancellationToken).ConfigureAwait(false);
        if (!passedExactSimulation)
        {
            throw Reject("account_opening_policy_simulation_required", "The exact account-opening policy must pass validation before approval.");
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
            TraceParent = System.Diagnostics.Activity.Current?.Id,
            TraceState = System.Diagnostics.Activity.Current?.TraceStateString,
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
        AddAudit(command.InstitutionId, command.ActorId, "institution.configuration.submit", "institution.account_opening_policy.approval.submit", record.VersionId, requestId, command.CorrelationId, now);
        await SaveAsync("account_opening_policy_submission_conflict", "The account-opening policy changed while approval was being submitted.", cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new AccountOpeningPolicySubmissionResult(
            record.InstitutionId, requestId, record.VersionId, record.ContentHash, eventId, SubmittedStatus, now, false));
    }

    private async Task<string?> PublishCoreAsync(
        PublishAccountOpeningPolicyCommand command,
        string publicationId,
        string versionId,
        string contentHash,
        CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(command.InstitutionId, cancellationToken).ConfigureAwait(false);
        ConfigurationVersionRecord record = await LoadVersionAsync(command.InstitutionId, versionId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(record.Status, ApprovedStatus, StringComparison.Ordinal))
        {
            throw Reject("account_opening_policy_version_not_publishable", "Only an approved account-opening policy can be published.");
        }

        if (!string.Equals(record.ContentHash, contentHash, StringComparison.Ordinal))
        {
            throw Reject("account_opening_policy_publication_stale", "The publication hash does not match the approved account-opening policy.");
        }

        if (string.IsNullOrWhiteSpace(record.ApprovalReference) || string.IsNullOrWhiteSpace(record.ApprovedByActorId))
        {
            throw Reject("account_opening_policy_approval_evidence_missing", "The approved version has no trusted Workflow decision evidence.");
        }

        if (string.Equals(record.CreatedByActorId, command.ActorId.Value, StringComparison.Ordinal))
        {
            throw Reject("account_opening_policy_self_publication", "The account-opening policy maker cannot publish their own version.");
        }

        ConfigurationContent storedContent = ConfigurationContent.Create(record.ContentJson);
        if (!string.Equals(storedContent.Hash, record.ContentHash, StringComparison.Ordinal))
        {
            throw Reject("account_opening_policy_content_integrity_failed", "The stored account-opening policy does not match its recorded hash.");
        }

        AccountOpeningPolicy parsed;
        try
        {
            parsed = AccountOpeningPolicy.Parse(record.ContentJson);
        }
        catch (ArgumentException exception)
        {
            throw Reject("account_opening_policy_invalid", exception.Message);
        }

        await EnsureProductsDoNotExceedCeilingAsync(record, parsed, cancellationToken).ConfigureAwait(false);

        ConfigurationEffectivePeriod proposed = ConfigurationEffectivePeriod.Create(command.EffectiveFrom, command.EffectiveTo);
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
            throw Reject("account_opening_policy_publication_period_overlaps", "Another published account-opening policy overlaps the requested period.");
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
        AddAudit(command.InstitutionId, command.ActorId, "institution.configuration.publish", "institution.account_opening_policy.publish", record.VersionId, publicationId, command.CorrelationId, now);
        await SaveAsync("account_opening_policy_publication_conflict", "The account-opening policy changed while it was being published.", cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new AccountOpeningPolicyPublicationResult(
            record.InstitutionId, publicationId, record.VersionId, record.ContentHash,
            proposed.EffectiveFrom, proposed.EffectiveTo, now, false));
    }

    /// <summary>
    /// A published product rule may never allow more accounts than the ceiling being published.
    /// Decision: P1-E04-D07.
    /// </summary>
    private async Task EnsureProductsDoNotExceedCeilingAsync(
        ConfigurationVersionRecord record,
        AccountOpeningPolicy policy,
        CancellationToken cancellationToken)
    {
        string[] publishedProductVersions = await _context.ConfigurationVersions
            .Where(candidate => candidate.InstitutionId == record.InstitutionId
                && candidate.Schema == "product_eligibility.v1"
                && candidate.Status == PublishedStatus)
            .Select(candidate => candidate.VersionId)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        foreach (string versionId in publishedProductVersions)
        {
            ConfigurationVersionRecord? productVersion = await _context.ConfigurationVersions
                .SingleOrDefaultAsync(candidate => candidate.VersionId == versionId, cancellationToken)
                .ConfigureAwait(false);
            if (productVersion is null)
            {
                continue;
            }

            ConfigurationContent content = ConfigurationContent.Create(productVersion.ContentJson);
            Domain.Products.ProductEligibilityRules rules;
            try
            {
                rules = Domain.Products.ProductEligibilityRules.FromConfigurationContent(content);
            }
            catch (ArgumentException)
            {
                throw Reject("account_opening_policy_product_rule_invalid", "A published product rule could not be validated against the ceiling.");
            }

            if (!rules.ExistingAccounts.AllowsAny
                && rules.ExistingAccounts.MaximumAccounts > policy.MaximumOpenAccounts)
            {
                throw Reject(
                    "account_opening_policy_below_product_limit",
                    "A published product rule allows more accounts than the ceiling being published.");
            }
        }
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
        return record ?? throw Reject(
            "account_opening_policy_version_not_found",
            "The account-opening policy version is not available in this institution.");
    }

    private async Task EnsureInstitutionActiveAsync(InstitutionId institutionId, CancellationToken cancellationToken)
    {
        InstitutionRecord? institution = await _context.Institutions
            .SingleOrDefaultAsync(record => record.InstitutionId == institutionId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (institution is null || !string.Equals(institution.Status, "active", StringComparison.Ordinal))
        {
            throw Reject("institution_inactive", "Account-opening policy configuration requires an active institution.");
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
            throw new AccountOpeningPolicyRejectedException(code, message, exception);
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

    private async Task<IdempotencyOutcome> ExecuteRecordedAsync(
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
                Area,
                command,
                outcome.Kind == IdempotencyOutcomeKind.Replayed ? "replayed" : "succeeded");
            return outcome;
        }
        catch (AccountOpeningPolicyRejectedException)
        {
            _metrics?.RecordConfigurationCommand(Area, command, "rejected");
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

    private static string SetId(InstitutionId institutionId) =>
        string.Concat("institution-account-opening-policy:", institutionId.Value);

    private static string ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string trimmed = value.Trim();
        if (trimmed.Length > 128 || trimmed.Contains('|'))
        {
            throw Reject("account_opening_policy_identifier_invalid", "A configuration identifier may contain at most 128 characters and no separator.");
        }

        return trimmed;
    }

    private static T Decode<T>(IdempotencyOutcome outcome, bool replayed) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (string.IsNullOrWhiteSpace(outcome.Result))
        {
            throw new InvalidOperationException("The configuration command did not produce an idempotent result.");
        }

        T? decoded = JsonSerializer.Deserialize<T>(outcome.Result);
        if (decoded is null)
        {
            throw new InvalidOperationException("The stored configuration evidence is not readable.");
        }

        return decoded switch
        {
            AccountOpeningPolicyDraftResult draft => (T)(object)(draft with { Replayed = replayed }),
            AccountOpeningPolicySimulationResult simulation => (T)(object)(simulation with { Replayed = replayed }),
            AccountOpeningPolicySubmissionResult submission => (T)(object)(submission with { Replayed = replayed }),
            AccountOpeningPolicyPublicationResult publication => (T)(object)(publication with { Replayed = replayed }),
            _ => decoded,
        };
    }

    private static AccountOpeningPolicyRejectedException Reject(string code, string message) => new(code, message);
}
