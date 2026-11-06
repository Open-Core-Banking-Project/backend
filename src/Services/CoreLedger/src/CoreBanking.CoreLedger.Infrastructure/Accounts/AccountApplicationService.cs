using System;
using System.Collections.Generic;
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
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Products;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using CoreBanking.CoreLedger.Infrastructure.Products;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Owns the account-application boundary: a draft application bound to one exact published product
/// version, then one authoritative eligibility evaluation recorded as immutable evidence. It creates
/// no account, account number, balance or posting. Decisions: P1-E04-D07, P1-E04-D09, P1-E04-D17.
/// </summary>
public sealed class AccountApplicationService : IAccountApplicationService
{
    private const string Schema = "product_eligibility.v1";
    private const string SavingsFamily = "savings";
    private const string DraftStatus = nameof(AccountApplicationStatus.Draft);
    private const int ClaimLifetimeSeconds = 60;

    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly AuthoritativeEligibilityFactsResolver _factsResolver;
    private readonly IAccountOpeningPolicySource _openingPolicies;
    private readonly IAccountHoldingsReader _holdings;
    private readonly ProductAccountMetrics? _metrics;

    public AccountApplicationService(
        CoreLedgerDbContext context,
        IdempotencyService idempotency,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        AuthoritativeEligibilityFactsResolver factsResolver,
        IAccountOpeningPolicySource openingPolicies,
        IAccountHoldingsReader holdings,
        ProductAccountMetrics? metrics = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _factsResolver = factsResolver ?? throw new ArgumentNullException(nameof(factsResolver));
        _openingPolicies = openingPolicies ?? throw new ArgumentNullException(nameof(openingPolicies));
        _holdings = holdings ?? throw new ArgumentNullException(nameof(holdings));
        _metrics = metrics;
    }

    public async Task<AccountApplicationCreatedResult> CreateAsync(
        CreateAccountApplicationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        string applicationId = ValidateIdentifier(command.ApplicationId, nameof(command.ApplicationId));
        string requestId = ValidateIdentifier(command.RequestId, nameof(command.RequestId));
        string customerId = ValidateIdentifier(command.CustomerId, nameof(command.CustomerId));
        string productId = ValidateIdentifier(command.ProductId, nameof(command.ProductId));
        string branchId = ValidateIdentifier(command.BranchId, nameof(command.BranchId));
        string purposeCode = ValidateIdentifier(command.AccountPurposeCode, nameof(command.AccountPurposeCode));
        string ownershipCode = ValidateIdentifier(command.OwnershipTypeCode, nameof(command.OwnershipTypeCode));
        string currencyCode = ValidateCurrency(command.CurrencyCode);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            applicationId, requestId, customerId, productId, branchId, currencyCode, purposeCode, ownershipCode));
        IdempotencyOutcome outcome = await ExecuteRecordedAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.application.create", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            "create",
            token => CreateCoreAsync(
                command, applicationId, requestId, customerId, productId, branchId, currencyCode, purposeCode, ownershipCode, now, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<AccountApplicationCreatedResult>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountEligibilityResult> EvaluateAsync(
        EvaluateAccountEligibilityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        string applicationId = ValidateIdentifier(command.ApplicationId, nameof(command.ApplicationId));
        DateTimeOffset evaluatedAt = _timeProvider.GetUtcNow();

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|', applicationId));
        IdempotencyOutcome outcome = await ExecuteRecordedAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.application.evaluate", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            "evaluate",
            token => EvaluateCoreAsync(command, applicationId, evaluatedAt, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<AccountEligibilityResult>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountEligibilityResult?> GetLatestEvaluationAsync(
        GetAccountApplicationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.InstitutionId);
        if (_tenantContext.CurrentInstitution != query.InstitutionId)
        {
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the application target.");
        }

        string applicationId = ValidateIdentifier(query.ApplicationId, nameof(query.ApplicationId));
        AccountApplicationRecord? application = await _context.AccountApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.AccountApplicationId == applicationId, cancellationToken)
            .ConfigureAwait(false);
        if (application is null)
        {
            return null;
        }

        AccountEligibilityEvaluationRecord? evaluation = await _context.AccountEligibilityEvaluations
            .AsNoTracking()
            .Where(record => record.AccountApplicationId == applicationId)
            .OrderByDescending(record => record.EvaluatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return evaluation is null
            ? null
            : ToResult(evaluation, ParseStatus(application.Status), application.Version, replayed: false);
    }

    private async Task<string?> CreateCoreAsync(
        CreateAccountApplicationCommand command,
        string applicationId,
        string requestId,
        string customerId,
        string productId,
        string branchId,
        string currencyCode,
        string purposeCode,
        string ownershipCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        InstitutionRecord? institution = await _context.Institutions
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (institution is null || !string.Equals(institution.Status, "active", StringComparison.Ordinal))
        {
            throw Reject("institution_inactive", "Account opening requires an active institution.");
        }

        ProductRecord product = await _context.Products
            .SingleOrDefaultAsync(x => x.ProductId == productId && x.Family == SavingsFamily, cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("product_not_found", "The product is not available in this institution.");

        (ConfigurationVersionRecord version, ConfigurationPublicationRecord publication) =
            await ResolveEffectiveProductVersionAsync(product, now, cancellationToken).ConfigureAwait(false);

        bool duplicate = await _context.AccountApplications
            .AnyAsync(record => record.AccountApplicationId == applicationId, cancellationToken)
            .ConfigureAwait(false);
        if (duplicate)
        {
            throw Reject("account_application_conflict", "The account-application identifier is already used in this institution.");
        }

        _context.AccountApplications.Add(new AccountApplicationRecord
        {
            InstitutionId = command.InstitutionId.Value,
            AccountApplicationId = applicationId,
            RequestId = requestId,
            CustomerId = customerId,
            ProductId = product.ProductId,
            ProductCode = product.Code,
            ProductFamily = product.Family,
            ProductVersionId = version.VersionId,
            ProductVersionNumber = version.VersionNumber,
            ProductContentHash = version.ContentHash,
            ProductEffectiveFrom = publication.EffectiveFrom,
            ProductEffectiveTo = publication.EffectiveTo,
            BranchId = branchId,
            CurrencyCode = currencyCode,
            AccountPurposeCode = purposeCode,
            OwnershipTypeCode = ownershipCode,
            Status = DraftStatus,
            CreatedByActorId = command.ActorId.Value,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
        AddAudit(command, AccountPermissions.ApplicationCreate.Value, "account.application.create", applicationId, version.ContentHash, now);
        await SaveAsync("account_application_conflict", "The account application could not be recorded.", cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Serialize(new AccountApplicationCreatedResult(
            applicationId,
            requestId,
            customerId,
            product.ProductId,
            product.Code,
            product.Family,
            version.VersionId,
            version.VersionNumber,
            version.ContentHash,
            publication.EffectiveFrom,
            publication.EffectiveTo,
            branchId,
            currencyCode,
            purposeCode,
            ownershipCode,
            AccountApplicationStatus.Draft,
            1,
            now,
            false));
    }

    private async Task<string?> EvaluateCoreAsync(
        EvaluateAccountEligibilityCommand command,
        string applicationId,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        AccountApplicationRecord application = await _context.AccountApplications
            .SingleOrDefaultAsync(record => record.AccountApplicationId == applicationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_application_not_found", "The account application is not available in this institution.");

        if (!string.Equals(application.Status, DraftStatus, StringComparison.Ordinal))
        {
            throw Reject(
                "account_application_not_evaluable",
                "Only a draft account application can be evaluated; an evaluated application keeps its recorded evidence.");
        }

        if (command.ExpectedVersion != application.Version)
        {
            throw Reject("account_application_version_mismatch", "The application version does not match the submitted expectation.");
        }

        if (string.IsNullOrWhiteSpace(command.ReasonCode))
        {
            throw Reject("account_application_reason_required", "Submitting an application requires a reason code.");
        }

        ProductRecord product = await _context.Products
            .SingleOrDefaultAsync(x => x.ProductId == application.ProductId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("product_not_found", "The product is not available in this institution.");
        (ConfigurationVersionRecord version, ConfigurationPublicationRecord publication) =
            await ResolveEffectiveProductVersionAsync(product, evaluatedAt, cancellationToken).ConfigureAwait(false);

        ConfigurationContent content = ConfigurationContent.Create(version.ContentJson);
        if (!string.Equals(content.Hash, version.ContentHash, StringComparison.Ordinal))
        {
            throw Reject("product_content_integrity_failed", "The effective product content does not match its recorded hash.");
        }

        ProductEligibilityRules rules;
        try
        {
            rules = ProductEligibilityRules.FromConfigurationContent(content);
        }
        catch (ArgumentException exception)
        {
            throw Reject("product_rule_invalid", "The effective product eligibility rules are invalid.", exception);
        }

        ResolvedEligibilityFacts facts = await _factsResolver
            .ResolveAsync(
                command.InstitutionId,
                application.CustomerId,
                application.BranchId,
                application.CurrencyCode,
                evaluatedAt,
                cancellationToken)
            .ConfigureAwait(false);
        if (facts.Facts is null)
        {
            throw Reject(
                facts.RejectionCode ?? "eligibility_facts_unavailable",
                "The authoritative customer, branch or currency facts are unavailable.");
        }

        AccountOpeningPolicyLookup policy = await _openingPolicies
            .GetAsync(command.InstitutionId, evaluatedAt, cancellationToken)
            .ConfigureAwait(false);
        if (policy.Policy is null)
        {
            throw Reject(
                policy.RejectionCode ?? AccountEligibilityPolicy.PolicyUnavailableCode,
                "The institution account-opening policy is unavailable.");
        }

        IReadOnlyCollection<AccountHolding> holdings = await _holdings
            .GetAsync(command.InstitutionId, application.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        AccountLimitBasis basis = AccountEligibilityPolicy.ResolveLimitBasis(
            rules,
            policy.Policy,
            application.ProductCode,
            application.ProductFamily,
            application.CurrencyCode);
        await AcquireClaimAsync(command, application, basis, evaluatedAt, cancellationToken).ConfigureAwait(false);

        AccountEligibilityDecision decision = AccountEligibilityPolicy.Decide(
            rules,
            facts.Facts,
            policy.Policy,
            holdings,
            application.ProductCode,
            application.ProductFamily,
            application.CurrencyCode,
            evaluatedAt);

        string evaluationId = Guid.NewGuid().ToString("N");
        AccountApplicationStatus nextStatus = AccountApplicationLifecycle.RecordEligibility(
            AccountApplicationLifecycle.BeginEligibilityEvaluation(ParseStatus(application.Status)),
            decision.Eligible);
        application.Status = nextStatus.ToString();
        application.Version++;
        application.UpdatedAt = evaluatedAt;

        AccountEligibilityEvaluationRecord evaluation = new()
        {
            InstitutionId = command.InstitutionId.Value,
            EvaluationId = evaluationId,
            AccountApplicationId = application.AccountApplicationId,
            ProductVersionId = version.VersionId,
            ProductContentHash = version.ContentHash,
            CustomerFactsVersion = decision.CustomerFactsVersion,
            BranchReferenceVersion = decision.BranchReferenceVersion,
            CurrencyReferenceVersion = decision.CurrencyReferenceVersion,
            FactsValidUntil = facts.Facts.ValidUntil,
            Eligible = decision.Eligible,
            FactsCurrent = decision.FactsCurrent,
            DecisionCode = decision.Code,
            TraceJson = JsonSerializer.Serialize(decision.Trace),
            LimitKey = decision.Limit?.LimitKey,
            ProductMaximum = decision.Limit?.ProductMaximum,
            CountedHoldings = decision.Limit?.Counted,
            EffectiveMaximum = decision.Limit?.EffectiveMaximum,
            CountedHoldingIdsJson = JsonSerializer.Serialize(decision.Limit?.CountedHoldingIds ?? []),
            PolicyVersionId = policy.ReferenceVersion,
            EvaluatedByActorId = command.ActorId.Value,
            EvaluatedAt = evaluatedAt,
        };
        _context.AccountEligibilityEvaluations.Add(evaluation);
        await ReleaseClaimAsync(application.AccountApplicationId, evaluatedAt, cancellationToken).ConfigureAwait(false);
        AddAudit(command, AccountPermissions.ApplicationCreate.Value, "account.application.evaluate", application.AccountApplicationId, evaluationId, evaluatedAt);
        await SaveAsync("account_evaluation_conflict", "The eligibility evaluation could not be recorded.", cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Serialize(ToResult(evaluation, nextStatus, application.Version, replayed: false));
    }

    private async Task<(ConfigurationVersionRecord Version, ConfigurationPublicationRecord Publication)>
        ResolveEffectiveProductVersionAsync(
            ProductRecord product,
            DateTimeOffset asOf,
            CancellationToken cancellationToken)
    {
        ConfigurationPublicationRecord[] effective = await _context.ConfigurationPublications
            .AsNoTracking()
            .Where(record => record.ConfigurationSetId == product.ConfigurationSetId
                && record.EffectiveFrom <= asOf
                && (record.EffectiveTo == null || asOf < record.EffectiveTo))
            .Take(2)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (effective.Length == 0)
        {
            throw Reject("product_configuration_not_effective", "No published product version is effective at the requested instant.");
        }

        if (effective.Length > 1)
        {
            throw Reject("product_configuration_ambiguous", "Multiple published product versions are effective at the requested instant.");
        }

        ConfigurationPublicationRecord publication = effective[0];
        ConfigurationVersionRecord version = await _context.ConfigurationVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.VersionId == publication.VersionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("product_publication_integrity_failed", "The effective publication has no matching product version.");
        if (!string.Equals(version.Schema, Schema, StringComparison.Ordinal)
            || !string.Equals(version.Status, "published", StringComparison.Ordinal)
            || !string.Equals(version.ConfigurationSetId, product.ConfigurationSetId, StringComparison.Ordinal)
            || !string.Equals(version.ContentHash, publication.ContentHash, StringComparison.Ordinal)
            || version.EffectiveFrom != publication.EffectiveFrom
            || version.EffectiveTo != publication.EffectiveTo)
        {
            throw Reject("product_publication_integrity_failed", "The effective publication does not match its immutable product version.");
        }

        return (version, publication);
    }

    /// <summary>
    /// Takes the short-lived opening claim for this customer and counting basis. Concurrent openings
    /// that share the basis collide on the unique index, so only one can pass the same limit.
    /// Decision: P1-E04-D07.
    /// </summary>
    private async Task AcquireClaimAsync(
        EvaluateAccountEligibilityCommand command,
        AccountApplicationRecord application,
        AccountLimitBasis basis,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        string limitKey = AccountOpeningPolicy.ToToken(basis.LimitKey);

        // A crashed evaluation must not block the customer forever, so an expired claim is released
        // before the new one is taken.
        AccountOpeningClaimRecord[] expired = await _context.AccountOpeningClaims
            .Where(claim => claim.CustomerId == application.CustomerId
                && claim.LimitKey == limitKey
                && claim.LimitKeyValue == basis.LimitKeyValue
                && claim.ReleasedAt == null
                && claim.ExpiresAt <= evaluatedAt)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (AccountOpeningClaimRecord claim in expired)
        {
            claim.ReleasedAt = evaluatedAt;
        }

        bool held = await _context.AccountOpeningClaims
            .AnyAsync(
                claim => claim.CustomerId == application.CustomerId
                    && claim.LimitKey == limitKey
                    && claim.LimitKeyValue == basis.LimitKeyValue
                    && claim.ReleasedAt == null
                    && claim.ExpiresAt > evaluatedAt,
                cancellationToken)
            .ConfigureAwait(false);
        if (held)
        {
            throw Reject(
                "account_opening_claim_busy",
                "Another opening for this customer is being evaluated against the same limit.");
        }

        _context.AccountOpeningClaims.Add(new AccountOpeningClaimRecord
        {
            InstitutionId = command.InstitutionId.Value,
            ClaimId = Guid.NewGuid().ToString("N"),
            CustomerId = application.CustomerId,
            LimitKey = limitKey,
            LimitKeyValue = basis.LimitKeyValue,
            AccountApplicationId = application.AccountApplicationId,
            AcquiredAt = evaluatedAt,
            ExpiresAt = evaluatedAt.AddSeconds(ClaimLifetimeSeconds),
        });

        // Committed before the count is read: this is the row that serialises a concurrent opening.
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            throw new AccountApplicationRejectedException(
                "account_opening_claim_busy",
                "Another opening for this customer is being evaluated against the same limit.",
                exception);
        }
    }

    private async Task ReleaseClaimAsync(string applicationId, DateTimeOffset releasedAt, CancellationToken cancellationToken)
    {
        AccountOpeningClaimRecord? claim = await _context.AccountOpeningClaims
            .SingleOrDefaultAsync(
                record => record.AccountApplicationId == applicationId && record.ReleasedAt == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (claim is not null)
        {
            claim.ReleasedAt = releasedAt;
        }
    }

    private static AccountEligibilityResult ToResult(
        AccountEligibilityEvaluationRecord evaluation,
        AccountApplicationStatus status,
        long version,
        bool replayed) =>
        new(
            evaluation.AccountApplicationId,
            status,
            version,
            evaluation.EvaluationId,
            evaluation.ProductVersionId,
            evaluation.ProductContentHash,
            evaluation.Eligible,
            evaluation.FactsCurrent,
            evaluation.DecisionCode,
            evaluation.CustomerFactsVersion,
            evaluation.BranchReferenceVersion,
            evaluation.CurrencyReferenceVersion,
            JsonSerializer.Deserialize<List<EligibilityRuleTrace>>(evaluation.TraceJson) ?? [],
            evaluation.LimitKey is null
                ? null
                : new AccountLimitAssessment(
                    evaluation.LimitKey,
                    evaluation.CountedHoldings ?? 0,
                    evaluation.ProductMaximum,
                    evaluation.EffectiveMaximum ?? 0,
                    evaluation.EffectiveMaximum ?? 0,
                    evaluation.DecisionCode != AccountEligibilityPolicy.LimitReachedCode,
                    JsonSerializer.Deserialize<List<string>>(evaluation.CountedHoldingIdsJson) ?? []),
            evaluation.EvaluatedAt,
            replayed);

    private static AccountApplicationStatus ParseStatus(string status) =>
        Enum.TryParse(status, ignoreCase: false, out AccountApplicationStatus parsed)
            ? parsed
            : throw new InvalidOperationException($"account_application_status_unknown:{status}");

    private async Task SaveAsync(string code, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            throw new AccountApplicationRejectedException(code, message, exception);
        }
    }

    private void AddAudit(
        CreateAccountApplicationCommand command,
        string permission,
        string action,
        string resourceId,
        string afterReference,
        DateTimeOffset occurredAt) =>
        AddAudit(
            command.InstitutionId,
            command.ActorId,
            permission,
            action,
            resourceId,
            afterReference,
            command.CorrelationId,
            occurredAt);

    private void AddAudit(
        EvaluateAccountEligibilityCommand command,
        string permission,
        string action,
        string resourceId,
        string afterReference,
        DateTimeOffset occurredAt) =>
        AddAudit(
            command.InstitutionId,
            command.ActorId,
            permission,
            action,
            resourceId,
            afterReference,
            command.CorrelationId,
            occurredAt);

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
            ResourceType = "account.application",
            ResourceId = resourceId,
            Outcome = "succeeded",
            Reason = string.Empty,
            CorrelationId = correlationId,
            AfterReference = afterReference,
            OccurredAt = occurredAt,
        });
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
            _metrics?.RecordAccountCommand(command, outcome.Kind == IdempotencyOutcomeKind.Replayed ? "replayed" : "succeeded");
            return outcome;
        }
        catch (AccountApplicationRejectedException)
        {
            _metrics?.RecordAccountCommand(command, "rejected");
            throw;
        }
    }

    private void ValidateTarget(InstitutionId institutionId, string idempotencyKey, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (_tenantContext.CurrentInstitution != institutionId)
        {
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the application target.");
        }
    }

    private static string ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string trimmed = value.Trim();
        if (trimmed.Length > 128 || trimmed.Contains('|'))
        {
            throw Reject("account_identifier_invalid", "An account identifier may contain at most 128 characters and no separator.");
        }

        return trimmed;
    }

    private static string ValidateCurrency(string currencyCode)
    {
        string code = currencyCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Length != 3 || !code.All(character => character is >= 'A' and <= 'Z'))
        {
            throw Reject("account_currency_invalid", "The account currency must be an ISO 4217 alpha-3 code.");
        }

        return code;
    }

    private static T Decode<T>(IdempotencyOutcome outcome, bool replayed) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (string.IsNullOrWhiteSpace(outcome.Result))
        {
            throw new InvalidOperationException("The account command did not produce an idempotent result.");
        }

        T? decoded = JsonSerializer.Deserialize<T>(outcome.Result);
        if (decoded is null)
        {
            throw new InvalidOperationException("The stored account command evidence is not readable.");
        }

        return decoded switch
        {
            AccountApplicationCreatedResult created => (T)(object)(created with { Replayed = replayed }),
            AccountEligibilityResult eligibility => (T)(object)(eligibility with { Replayed = replayed }),
            _ => decoded,
        };
    }

    private static AccountApplicationRejectedException Reject(string code, string message) => new(code, message);

    private static AccountApplicationRejectedException Reject(string code, string message, Exception inner) =>
        new(code, message, inner);
}
