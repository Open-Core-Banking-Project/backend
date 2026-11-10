using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Products;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Activates an open account only when the ledger proves its initialisation, the approval evidence is bound
/// to the exact application version and authoritative facts are still fresh. Activation writes no balance,
/// journal line or hold. Decision: P1-E04-D09.
/// </summary>
public sealed class AccountActivationService : IAccountActivationService
{
    private const string Producer = "core-ledger";
    private const string AccountEventFamily = "account";
    private const string AccountActivatedEventType = "account_activated";
    private const int AccountSchemaVersion = 1;
    private const string InitialisedStatus = "initialised";

    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly AuthoritativeEligibilityFactsResolver _factsResolver;
    private readonly IAccountNumberSchemeSource _schemes;

    public AccountActivationService(
        CoreLedgerDbContext context,
        IdempotencyService idempotency,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        AuthoritativeEligibilityFactsResolver factsResolver,
        IAccountNumberSchemeSource schemes)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _factsResolver = factsResolver ?? throw new ArgumentNullException(nameof(factsResolver));
        _schemes = schemes ?? throw new ArgumentNullException(nameof(schemes));
    }

    public async Task<AccountActivatedResult> ActivateAsync(
        ActivateAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.InstitutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.AccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        if (_tenantContext.CurrentInstitution != command.InstitutionId)
        {
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the account target.");
        }

        string accountId = command.AccountId.Trim();
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|', accountId, command.ExpectedVersion));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.activate", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => ActivateCoreAsync(command, accountId, token),
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(outcome.Result))
        {
            throw new InvalidOperationException("The activation did not produce an idempotent result.");
        }

        AccountActivatedResult result = System.Text.Json.JsonSerializer.Deserialize<AccountActivatedResult>(outcome.Result)
            ?? throw new InvalidOperationException("The stored activation evidence is not readable.");
        return result with { Replayed = outcome.Kind == IdempotencyOutcomeKind.Replayed };
    }

    private async Task<string?> ActivateCoreAsync(
        ActivateAccountCommand command,
        string accountId,
        CancellationToken cancellationToken)
    {
        AccountRecord account = await _context.Accounts
            .SingleOrDefaultAsync(record => record.AccountId == accountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_not_found", "The account is not available in this institution.");

        if (command.ExpectedVersion != account.Version)
        {
            throw Reject("account_version_mismatch", "The account version does not match the submitted expectation.");
        }

        if (string.Equals(account.Status, nameof(AccountApplicationStatus.Active), StringComparison.Ordinal))
        {
            return Serialise(account, replayed: false);
        }

        if (!string.Equals(account.Status, nameof(AccountApplicationStatus.Open), StringComparison.Ordinal))
        {
            throw Reject("account_not_activatable", "Only an open account can be activated.");
        }

        if (!string.Equals(account.LedgerInitialisationStatus, InitialisedStatus, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(account.LedgerAccountReference))
        {
            throw Reject(
                "ledger_initialisation_unproven",
                "Activation requires a ledger response that proves the ledger account exists.");
        }

        AccountApplicationRecord application = await _context.AccountApplications
            .SingleOrDefaultAsync(
                record => record.AccountApplicationId == account.AccountApplicationId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_approval_evidence_missing", "The account has no application record.");

        if (string.IsNullOrWhiteSpace(application.ApprovedByActorId) || application.ApprovedAt is null
            || string.IsNullOrWhiteSpace(application.ApprovalRequestId))
        {
            throw Reject("account_approval_evidence_missing", "Activation requires the bound approval evidence.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        AccountNumberSchemeLookup scheme = await _schemes
            .GetAsync(command.InstitutionId, now, cancellationToken)
            .ConfigureAwait(false);
        if (scheme.Scheme is null
            || !string.Equals(scheme.VersionId, account.SchemeVersionId, StringComparison.Ordinal))
        {
            throw Reject(
                "account_number_scheme_not_effective",
                "The scheme that issued this number is no longer the effective issuing scheme.");
        }

        // Activation re-reads authoritative facts inside the approved freshness ceiling.
        ResolvedEligibilityFacts facts = await _factsResolver
            .ResolveAsync(
                command.InstitutionId,
                account.CustomerId,
                account.BranchId,
                account.CurrencyCode,
                now,
                cancellationToken)
            .ConfigureAwait(false);
        if (facts.Facts is null)
        {
            throw Reject(
                facts.RejectionCode ?? "eligibility_facts_unavailable",
                "Activation requires current authoritative facts.");
        }

        ProductEligibilityRules rules = await ResolveEffectiveRulesAsync(account, cancellationToken).ConfigureAwait(false);
        FinalEligibilityDecision decision = ProductEligibilityApprovalPolicy.RecheckForFinalApproval(rules, facts.Facts, now);
        if (!decision.Eligible || !decision.FactsCurrent)
        {
            throw Reject(
                decision.Code,
                "Activation requires a current eligible decision; the account stays open and unactivated.");
        }

        AccountApplicationStatus nextStatus = AccountApplicationLifecycle.RecordActivated(ParseStatus(application.Status));
        account.Status = nameof(AccountApplicationStatus.Active);
        account.Version++;
        application.Status = nextStatus.ToString();
        application.Version++;
        application.UpdatedAt = now;

        _context.AccountStatusHistory.Add(new AccountStatusHistoryRecord
        {
            InstitutionId = command.InstitutionId.Value,
            HistoryId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            Status = account.Status,
            ReasonCode = "account_activated",
            ActorId = command.ActorId.Value,
            OccurredAt = now,
        });
        AddAudit(command, account, now);
        AddOutboxFact(command, account, now);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            throw new AccountApplicationRejectedException(
                "account_activation_conflict",
                "The account changed while it was being activated.",
                exception);
        }

        return Serialise(account, replayed: false);
    }

    private async Task<ProductEligibilityRules> ResolveEffectiveRulesAsync(
        AccountRecord account,
        CancellationToken cancellationToken)
    {
        ConfigurationVersionRecord? version = await _context.ConfigurationVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.VersionId == account.ProductVersionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("product_version_not_found", "The account product version is not available.");
        if (!string.Equals(version.ContentHash, account.ProductContentHash, StringComparison.Ordinal))
        {
            throw Reject("product_content_integrity_failed", "The account product version no longer matches its recorded hash.");
        }

        ConfigurationContent content = ConfigurationContent.Create(version.ContentJson);
        try
        {
            return ProductEligibilityRules.FromConfigurationContent(content);
        }
        catch (ArgumentException exception)
        {
            throw Reject("product_rule_invalid", "The account product rules are invalid.", exception);
        }
    }

    private void AddAudit(ActivateAccountCommand command, AccountRecord account, DateTimeOffset now)
    {
        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = command.InstitutionId.Value,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = command.ActorId.Value,
            ActorType = "human",
            Permission = AccountPermissions.ActivationCreate.Value,
            Action = "account.activation.create",
            ResourceType = "account.account",
            ResourceId = account.AccountId,
            Outcome = "succeeded",
            Reason = string.Empty,
            CorrelationId = command.CorrelationId,
            BeforeReference = account.MaskedAccountNumber,
            OccurredAt = now,
        });
    }

    private void AddOutboxFact(ActivateAccountCommand command, AccountRecord account, DateTimeOffset now)
    {
        string eventId = Guid.NewGuid().ToString("N");
        AccountActivatedV1 payload = new(
            account.InstitutionId,
            account.AccountId,
            account.CustomerId,
            account.ProductVersionId,
            account.Version,
            now);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = AccountActivatedEventType,
            SchemaVersion = AccountSchemaVersion,
            OccurredAt = now,
            InstitutionId = account.InstitutionId,
            CorrelationId = command.CorrelationId,
            Producer = Producer,
            BusinessReference = account.AccountId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = account.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = AccountActivatedEventType,
            SchemaVersion = AccountSchemaVersion,
            Topic = EventContract.TopicFor(Producer, AccountEventFamily, AccountSchemaVersion),
            BusinessReference = account.AccountId,
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
    }

    private static string Serialise(AccountRecord account, bool replayed) =>
        System.Text.Json.JsonSerializer.Serialize(new AccountActivatedResult(
            account.AccountId,
            account.MaskedAccountNumber,
            AccountApplicationStatus.Active,
            account.LedgerAccountReference ?? string.Empty,
            account.Version,
            account.OpenedAt,
            replayed));

    private static AccountApplicationStatus ParseStatus(string status) =>
        Enum.TryParse(status, ignoreCase: false, out AccountApplicationStatus parsed)
            ? parsed
            : throw new InvalidOperationException($"account_application_status_unknown:{status}");

    private static AccountApplicationRejectedException Reject(string code, string message) => new(code, message);

    private static AccountApplicationRejectedException Reject(string code, string message, Exception inner) =>
        new(code, message, inner);
}
