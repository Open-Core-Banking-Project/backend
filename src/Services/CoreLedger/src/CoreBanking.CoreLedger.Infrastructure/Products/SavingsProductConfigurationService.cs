using System;
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
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Products;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Products;

public sealed class SavingsProductConfigurationService : ISavingsProductConfigurationService
{
    private const string Area = "product_eligibility";
    private const string Schema = "product_eligibility.v1";
    private const string Producer = "core-ledger";
    private const string ApprovalEventFamily = "approval";
    private const string ApprovalRequestedEventType = "approval_requested";
    private const int ApprovalSchemaVersion = 1;
    private const string ProductEventFamily = "product";
    private const string ProductPublishedEventType = "product_version_published";
    private const int ProductSchemaVersion = 1;

    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public SavingsProductConfigurationService(
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

    public async Task<SavingsEligibilityDraftResult> CreateDraftAsync(
        CreateSavingsEligibilityDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        string productId = ValidateIdentifier(command.ProductId, nameof(command.ProductId), 96);
        string versionId = ValidateIdentifier(command.VersionId, nameof(command.VersionId), 128);
        ArgumentNullException.ThrowIfNull(command.Rules);
        ProductCode code = ProductCode.From(command.ProductCode);
        InstitutionProduct.Create(command.InstitutionId, code, ProductFamily.Savings);
        string configurationSetId = $"product-eligibility:{productId}";
        ConfigurationContent content = command.Rules.ToConfigurationContent();
        InstitutionConfigurationVersion version = InstitutionConfigurationVersion.Create(
            command.InstitutionId,
            configurationSetId,
            versionId,
            command.VersionNumber,
            Area,
            code.Value,
            content,
            command.ActorId);
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            productId, code.Value, versionId, command.VersionNumber, content.Hash));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "product.savings.eligibility.draft.create", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => CreateDraftCoreAsync(productId, code, version, token),
            cancellationToken).ConfigureAwait(false);
        return DecodeDraft(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<SavingsEligibilitySimulationCommandResult> SimulateAsync(
        SimulateSavingsEligibilityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        string versionId = ValidateIdentifier(command.VersionId, nameof(command.VersionId), 128);
        string simulationId = ValidateIdentifier(command.SimulationId, nameof(command.SimulationId), 128);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ContentHash);
        ArgumentNullException.ThrowIfNull(command.Scenarios);
        string fingerprint = ConfigurationContent.Create(JsonSerializer.Serialize(new
        {
            versionId,
            simulationId,
            contentHash = command.ContentHash.Trim(),
            scenarios = command.Scenarios.OrderBy(x => x.ScenarioId, StringComparer.Ordinal),
        })).Hash;
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "product.savings.eligibility.simulate", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => SimulateCoreAsync(versionId, simulationId, command.ContentHash.Trim(), command.Scenarios, token),
            cancellationToken).ConfigureAwait(false);
        return DecodeSimulation(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<SavingsEligibilityApprovalSubmissionResult> SubmitForApprovalAsync(
        SubmitSavingsEligibilityForApprovalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        string requestId = ValidateIdentifier(command.RequestId, nameof(command.RequestId), 128);
        string sourceRequestKey = ValidateIdentifier(command.SourceRequestKey, nameof(command.SourceRequestKey), 128);
        string versionId = ValidateIdentifier(command.VersionId, nameof(command.VersionId), 128);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ContentHash);
        string contentHash = command.ContentHash.Trim().ToLowerInvariant();
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|', requestId, sourceRequestKey, versionId, contentHash));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "product.savings.eligibility.approval.submit", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => SubmitForApprovalCoreAsync(command, requestId, sourceRequestKey, versionId, contentHash, token),
            cancellationToken).ConfigureAwait(false);
        return DecodeSubmission(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<SavingsEligibilityPublicationResult> PublishAsync(
        PublishSavingsEligibilityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        string publicationId = ValidateIdentifier(command.PublicationId, nameof(command.PublicationId), 128);
        string versionId = ValidateIdentifier(command.VersionId, nameof(command.VersionId), 128);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ContentHash);
        string contentHash = command.ContentHash.Trim().ToLowerInvariant();
        ConfigurationEffectivePeriod.Create(command.EffectiveFrom, command.EffectiveTo);
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            publicationId,
            versionId,
            contentHash,
            command.EffectiveFrom.ToString("O", CultureInfo.InvariantCulture),
            command.EffectiveTo?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "product.savings.eligibility.publish", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => PublishCoreAsync(command, publicationId, versionId, contentHash, token),
            cancellationToken).ConfigureAwait(false);
        return DecodePublication(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    private async Task<string?> CreateDraftCoreAsync(
        string productId,
        ProductCode code,
        InstitutionConfigurationVersion version,
        CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(cancellationToken).ConfigureAwait(false);
        ProductRecord? product = await _context.Products
            .SingleOrDefaultAsync(x => x.ProductId == productId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (product is null)
        {
            _context.ConfigurationSets.Add(new ConfigurationSetRecord
            {
                InstitutionId = version.InstitutionId.Value,
                ConfigurationSetId = version.ConfigurationSetId,
                Area = version.Area,
                Scope = version.Scope,
                Version = 1,
                CreatedAt = now,
            });
            _context.Products.Add(new ProductRecord
            {
                InstitutionId = version.InstitutionId.Value,
                ProductId = productId,
                Code = code.Value,
                Family = "savings",
                ConfigurationSetId = version.ConfigurationSetId,
                CreatedByActorId = version.CreatedBy.Value,
                Version = 1,
                CreatedAt = now,
            });
        }
        else if (!string.Equals(product.Code, code.Value, StringComparison.Ordinal)
            || !string.Equals(product.Family, "savings", StringComparison.Ordinal)
            || !string.Equals(product.ConfigurationSetId, version.ConfigurationSetId, StringComparison.Ordinal))
        {
            throw Reject("product_identity_conflict", "The product identifier is already bound to a different code or family.");
        }

        _context.ConfigurationVersions.Add(new ConfigurationVersionRecord
        {
            InstitutionId = version.InstitutionId.Value,
            VersionId = version.VersionId,
            ConfigurationSetId = version.ConfigurationSetId,
            VersionNumber = version.VersionNumber,
            Schema = Schema,
            ContentJson = version.Content.CanonicalJson,
            ContentHash = version.Content.Hash,
            Status = "draft",
            CreatedByActorId = version.CreatedBy.Value,
            Version = 1,
            CreatedAt = now,
        });
        await SaveAsync("product_version_conflict", "The product code, version identifier or version number is already in use.", cancellationToken).ConfigureAwait(false);
        return EncodeDraft(version.InstitutionId.Value, productId, code.Value, version.VersionId, version.VersionNumber, version.Content.Hash, "draft", now);
    }

    private async Task<string?> SimulateCoreAsync(
        string versionId,
        string simulationId,
        string contentHash,
        System.Collections.Generic.IReadOnlyCollection<ProductEligibilitySimulationInput> scenarios,
        CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(cancellationToken).ConfigureAwait(false);
        ConfigurationVersionRecord record = await _context.ConfigurationVersions
            .SingleOrDefaultAsync(x => x.VersionId == versionId, cancellationToken).ConfigureAwait(false)
            ?? throw Reject("product_version_not_found", "The product version is not available in this institution.");
        if (!string.Equals(record.Schema, Schema, StringComparison.Ordinal)
            || !string.Equals(record.Status, "draft", StringComparison.Ordinal))
            throw Reject("product_version_not_simulatable", "Only a draft Savings eligibility version can be simulated.");
        if (!string.Equals(record.ContentHash, contentHash, StringComparison.Ordinal))
            throw Reject("product_simulation_stale", "The requested simulation hash does not match the stored product version.");

        ConfigurationContent content = ConfigurationContent.Create(record.ContentJson);
        if (!string.Equals(content.Hash, record.ContentHash, StringComparison.Ordinal))
            throw Reject("product_content_integrity_failed", "The stored product content does not match its recorded hash.");
        ProductEligibilityRules rules;
        try
        {
            rules = ProductEligibilityRules.FromConfigurationContent(content);
        }
        catch (ArgumentException exception)
        {
            throw new SavingsProductConfigurationRejectedException("product_rule_invalid", "The stored product eligibility rules are invalid.", exception);
        }

        ProductEligibilitySimulationResult simulation;
        try
        {
            simulation = ProductEligibilitySimulator.Simulate(
                simulationId, rules, scenarios, _timeProvider.GetUtcNow());
        }
        catch (ArgumentException exception)
        {
            throw new SavingsProductConfigurationRejectedException("product_simulation_input_invalid", exception.Message, exception);
        }
        _context.ConfigurationSimulations.Add(new ConfigurationSimulationRecord
        {
            InstitutionId = record.InstitutionId,
            SimulationId = simulation.SimulationId,
            VersionId = record.VersionId,
            DatasetVersion = simulation.DatasetVersion,
            Engine = "product-eligibility-v1",
            ContentHash = simulation.ContentHash,
            InputHash = simulation.InputHash,
            OutputHash = simulation.OutputHash,
            Outcome = simulation.Passed ? "passed" : "failed",
            SimulatedAt = simulation.SimulatedAt,
        });
        await SaveAsync("product_simulation_conflict", "The simulation identifier or exact input was already recorded.", cancellationToken).ConfigureAwait(false);
        return EncodeSimulation(
            record.InstitutionId, record.VersionId, simulation.SimulationId, simulation.ContentHash,
            simulation.InputHash, simulation.OutputHash, simulation.Passed ? "passed" : "failed", simulation.SimulatedAt);
    }

    private async Task<string?> SubmitForApprovalCoreAsync(
        SubmitSavingsEligibilityForApprovalCommand command,
        string requestId,
        string sourceRequestKey,
        string versionId,
        string contentHash,
        CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(cancellationToken).ConfigureAwait(false);
        ConfigurationVersionRecord record = await _context.ConfigurationVersions
            .SingleOrDefaultAsync(x => x.VersionId == versionId, cancellationToken).ConfigureAwait(false)
            ?? throw Reject("product_version_not_found", "The product version is not available in this institution.");
        if (!string.Equals(record.Schema, Schema, StringComparison.Ordinal)
            || !string.Equals(record.Status, "draft", StringComparison.Ordinal))
            throw Reject("product_version_not_submittable", "Only a draft Savings eligibility version can be submitted.");
        if (!string.Equals(record.ContentHash, contentHash, StringComparison.Ordinal))
            throw Reject("product_approval_stale", "The approval submission hash does not match the stored product version.");
        bool passedExactSimulation = await _context.ConfigurationSimulations.AnyAsync(x =>
            x.VersionId == record.VersionId && x.ContentHash == record.ContentHash && x.Outcome == "passed",
            cancellationToken).ConfigureAwait(false);
        if (!passedExactSimulation)
            throw Reject("product_simulation_required", "The exact Savings eligibility version must pass simulation before approval.");

        DateTimeOffset now = _timeProvider.GetUtcNow();
        ApprovalRequestedV1 payload = new(
            record.InstitutionId, requestId, sourceRequestKey,
            SavingsEligibilityApprovalV1.WorkflowKey,
            SavingsEligibilityApprovalV1.WorkflowVersionId,
            SavingsEligibilityApprovalV1.WorkflowDefinitionHash,
            SavingsEligibilityApprovalV1.SourceService,
            SavingsEligibilityApprovalV1.SubjectType,
            record.VersionId,
            record.VersionNumber.ToString(CultureInfo.InvariantCulture),
            record.ContentHash,
            command.ActorId.Value,
            SavingsEligibilityApprovalV1.RequiredPermission,
            command.CorrelationId,
            now);
        string eventId = Guid.NewGuid().ToString("N");
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
        record.Status = "submitted";
        record.ApprovalReference = requestId;
        record.Version++;
        await SaveAsync("product_approval_conflict", "The product version changed while approval was being submitted.", cancellationToken)
            .ConfigureAwait(false);
        return EncodeSubmission(record.InstitutionId, requestId, record.VersionId, record.ContentHash, eventId, "submitted", now);
    }

    private async Task<string?> PublishCoreAsync(
        PublishSavingsEligibilityCommand command,
        string publicationId,
        string versionId,
        string contentHash,
        CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(cancellationToken).ConfigureAwait(false);
        ConfigurationVersionRecord record = await _context.ConfigurationVersions
            .SingleOrDefaultAsync(x => x.VersionId == versionId, cancellationToken).ConfigureAwait(false)
            ?? throw Reject("product_version_not_found", "The product version is not available in this institution.");
        if (!string.Equals(record.Schema, Schema, StringComparison.Ordinal)
            || !string.Equals(record.Status, "approved", StringComparison.Ordinal))
            throw Reject("product_version_not_publishable", "Only an approved Savings eligibility version can be published.");
        if (!string.Equals(record.ContentHash, contentHash, StringComparison.Ordinal))
            throw Reject("product_publication_stale", "The publication hash does not match the approved product version.");
        if (string.IsNullOrWhiteSpace(record.ApprovalReference) || string.IsNullOrWhiteSpace(record.ApprovedByActorId))
            throw Reject("product_approval_evidence_missing", "The approved version has no trusted Workflow decision evidence.");
        if (string.Equals(record.CreatedByActorId, command.ActorId.Value, StringComparison.Ordinal))
            throw Reject("product_self_publication", "The product version maker cannot publish their own version.");

        ConfigurationContent storedContent = ConfigurationContent.Create(record.ContentJson);
        if (!string.Equals(storedContent.Hash, record.ContentHash, StringComparison.Ordinal))
            throw Reject("product_content_integrity_failed", "The stored product content does not match its recorded hash.");
        try
        {
            ProductEligibilityRules.FromConfigurationContent(storedContent);
        }
        catch (ArgumentException exception)
        {
            throw new SavingsProductConfigurationRejectedException(
                "product_rule_invalid", "The approved product eligibility rules are invalid.", exception);
        }

        ConfigurationEffectivePeriod proposed = ConfigurationEffectivePeriod.Create(command.EffectiveFrom, command.EffectiveTo);
        var publicationPeriods = await _context.ConfigurationPublications
            .Where(x => x.ConfigurationSetId == record.ConfigurationSetId)
            .Select(x => new { x.VersionId, x.EffectiveFrom, x.EffectiveTo })
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        PublishedConfigurationVersion[] existing = publicationPeriods
            .Select(x => new PublishedConfigurationVersion(
                x.VersionId,
                ConfigurationEffectivePeriod.Create(x.EffectiveFrom, x.EffectiveTo)))
            .ToArray();
        try
        {
            ConfigurationPublicationPolicy.EnsureNoOverlap(existing, proposed);
        }
        catch (InvalidOperationException exception)
        {
            throw new SavingsProductConfigurationRejectedException(
                "product_effective_period_conflict", "The effective period overlaps an existing published version.", exception);
        }

        ConfigurationSetRecord configurationSet = await _context.ConfigurationSets
            .SingleAsync(x => x.ConfigurationSetId == record.ConfigurationSetId, cancellationToken).ConfigureAwait(false);
        ProductRecord product = await _context.Products
            .SingleAsync(x => x.ConfigurationSetId == record.ConfigurationSetId, cancellationToken).ConfigureAwait(false);
        configurationSet.Version++;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        record.Status = "published";
        record.EffectiveFrom = proposed.EffectiveFrom;
        record.EffectiveTo = proposed.EffectiveTo;
        record.PublishedAt = now;
        record.Version++;
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
        string eventId = AddProductOutbox(
            record,
            product,
            command.ActorId,
            command.CorrelationId,
            proposed,
            now);
        await SaveAsync(
            "product_publication_conflict",
            "The product version or effective schedule changed while publication was being processed.",
            cancellationToken).ConfigureAwait(false);
        return EncodePublication(
            record.InstitutionId,
            publicationId,
            product.ProductId,
            record.VersionId,
            record.ContentHash,
            eventId,
            record.Status,
            proposed.EffectiveFrom,
            proposed.EffectiveTo,
            now);
    }

    private string AddProductOutbox(
        ConfigurationVersionRecord version,
        ProductRecord product,
        ActorId publisher,
        string correlationId,
        ConfigurationEffectivePeriod period,
        DateTimeOffset publishedAt)
    {
        string eventId = Guid.NewGuid().ToString("N");
        ProductVersionPublishedV1 payload = new(
            version.InstitutionId,
            product.ProductId,
            product.Code,
            product.Family,
            version.VersionId,
            version.VersionNumber,
            version.ContentHash,
            period.EffectiveFrom,
            period.EffectiveTo,
            publisher.Value,
            version.ApprovalReference!,
            publishedAt);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = ProductPublishedEventType,
            SchemaVersion = ProductSchemaVersion,
            OccurredAt = publishedAt,
            InstitutionId = version.InstitutionId,
            CorrelationId = correlationId,
            Producer = Producer,
            BusinessReference = product.ProductId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = version.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = ProductPublishedEventType,
            SchemaVersion = ProductSchemaVersion,
            Topic = EventContract.TopicFor(Producer, ProductEventFamily, ProductSchemaVersion),
            BusinessReference = product.ProductId,
            CorrelationId = correlationId,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = publishedAt,
            CreatedAt = publishedAt,
        });
        return eventId;
    }

    private async Task EnsureInstitutionActiveAsync(CancellationToken cancellationToken)
    {
        InstitutionRecord? institution = await _context.Institutions.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (institution is null || !string.Equals(institution.Status, "active", StringComparison.Ordinal))
            throw Reject("institution_inactive", "Savings product configuration requires an active institution.");
    }

    private async Task SaveAsync(string code, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new SavingsProductConfigurationRejectedException(code, message, exception);
        }
        catch (DbUpdateException exception)
        {
            throw new SavingsProductConfigurationRejectedException(code, message, exception);
        }
    }

    private void ValidateTarget(InstitutionId institutionId, string idempotencyKey, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (_tenantContext.CurrentInstitution != institutionId)
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the product target.");
    }

    private static string ValidateIdentifier(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string trimmed = value.Trim();
        if (trimmed.Length > maximumLength || trimmed.Any(character =>
            character is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-' or ':')))
            throw new ArgumentException("Identifier contains unsupported characters or exceeds its maximum length.", parameterName);
        return trimmed;
    }

    private static SavingsProductConfigurationRejectedException Reject(string code, string message) => new(code, message);

    private static string EncodeDraft(
        string institutionId, string productId, string productCode, string versionId, int versionNumber,
        string contentHash, string status, DateTimeOffset changedAt) =>
        string.Join('|', institutionId, productId, productCode, versionId, versionNumber.ToString(CultureInfo.InvariantCulture),
            contentHash, status, changedAt.ToString("O", CultureInfo.InvariantCulture));

    private static string EncodeSimulation(
        string institutionId, string versionId, string simulationId, string contentHash, string inputHash,
        string outputHash, string outcome, DateTimeOffset simulatedAt) =>
        string.Join('|', institutionId, versionId, simulationId, contentHash, inputHash, outputHash, outcome,
            simulatedAt.ToString("O", CultureInfo.InvariantCulture));

    private static string EncodeSubmission(
        string institutionId, string requestId, string versionId, string contentHash, string eventId,
        string status, DateTimeOffset submittedAt) =>
        string.Join('|', institutionId, requestId, versionId, contentHash, eventId, status,
            submittedAt.ToString("O", CultureInfo.InvariantCulture));

    private static string EncodePublication(
        string institutionId,
        string publicationId,
        string productId,
        string versionId,
        string contentHash,
        string eventId,
        string status,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        DateTimeOffset publishedAt) =>
        string.Join('|', institutionId, publicationId, productId, versionId, contentHash, eventId, status,
            effectiveFrom.ToString("O", CultureInfo.InvariantCulture),
            effectiveTo?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            publishedAt.ToString("O", CultureInfo.InvariantCulture));

    private static SavingsEligibilityDraftResult DecodeDraft(IdempotencyOutcome outcome, bool replayed)
    {
        EnsureExecuted(outcome, "product data");
        string[] parts = (outcome.Result ?? string.Empty).Split('|');
        if (parts.Length != 8
            || !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out int versionNumber)
            || !DateTimeOffset.TryParseExact(parts[7], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset changedAt))
            throw new InvalidOperationException("The recorded idempotency outcome is not a Savings product draft outcome.");
        return new SavingsEligibilityDraftResult(parts[0], parts[1], parts[2], parts[3], versionNumber, parts[5], parts[6], changedAt, replayed);
    }

    private static SavingsEligibilitySimulationCommandResult DecodeSimulation(IdempotencyOutcome outcome, bool replayed)
    {
        EnsureExecuted(outcome, "simulation data");
        string[] parts = (outcome.Result ?? string.Empty).Split('|');
        if (parts.Length != 8
            || !DateTimeOffset.TryParseExact(parts[7], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset simulatedAt))
            throw new InvalidOperationException("The recorded idempotency outcome is not a Savings simulation outcome.");
        return new SavingsEligibilitySimulationCommandResult(
            parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], parts[6], simulatedAt, replayed);
    }

    private static SavingsEligibilityApprovalSubmissionResult DecodeSubmission(IdempotencyOutcome outcome, bool replayed)
    {
        EnsureExecuted(outcome, "approval submission data");
        string[] parts = (outcome.Result ?? string.Empty).Split('|');
        if (parts.Length != 7
            || !DateTimeOffset.TryParseExact(parts[6], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset submittedAt))
            throw new InvalidOperationException("The recorded idempotency outcome is not a Savings approval submission outcome.");
        return new SavingsEligibilityApprovalSubmissionResult(
            parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], submittedAt, replayed);
    }

    private static SavingsEligibilityPublicationResult DecodePublication(IdempotencyOutcome outcome, bool replayed)
    {
        EnsureExecuted(outcome, "publication data");
        string[] parts = (outcome.Result ?? string.Empty).Split('|');
        if (parts.Length != 10
            || !DateTimeOffset.TryParseExact(parts[7], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset effectiveFrom)
            || (!string.IsNullOrEmpty(parts[8])
                && !DateTimeOffset.TryParseExact(parts[8], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            || !DateTimeOffset.TryParseExact(parts[9], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset publishedAt))
            throw new InvalidOperationException("The recorded idempotency outcome is not a Savings publication outcome.");
        DateTimeOffset? effectiveTo = string.IsNullOrEmpty(parts[8])
            ? null
            : DateTimeOffset.ParseExact(parts[8], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return new SavingsEligibilityPublicationResult(
            parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], parts[6],
            effectiveFrom, effectiveTo, publishedAt, replayed);
    }

    private static void EnsureExecuted(IdempotencyOutcome outcome, string subject)
    {
        if (outcome.Kind == IdempotencyOutcomeKind.Conflict)
            throw Reject("idempotency_key_reused_with_different_request", $"The idempotency key was already used with different {subject}.");
        if (outcome.Kind == IdempotencyOutcomeKind.InProgress)
            throw new SavingsProductConfigurationInProgressException("An equivalent Savings product request is still in progress.");
    }
}
