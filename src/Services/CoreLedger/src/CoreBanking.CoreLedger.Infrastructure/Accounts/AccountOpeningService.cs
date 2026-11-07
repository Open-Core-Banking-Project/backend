using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Creates an account from an approved application in one transaction: number reservation, account row,
/// status history, audit and the <c>AccountOpenedV1</c> outbox fact. No balance, journal line or hold is
/// written. Decisions: P1-E04-D06, P1-E04-D09.
/// </summary>
public sealed class AccountOpeningService : IAccountOpeningService
{
    private const string Producer = "core-ledger";
    private const string AccountEventFamily = "account";
    private const string AccountOpenedEventType = "account_opened";
    private const int AccountSchemaVersion = 1;
    private const string NotInitialised = "not_initialised";

    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly IAccountNumberSchemeSource _schemes;
    private readonly IAccountNumberAllocator _allocator;

    public AccountOpeningService(
        CoreLedgerDbContext context,
        IdempotencyService idempotency,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        IAccountNumberSchemeSource schemes,
        IAccountNumberAllocator allocator)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _schemes = schemes ?? throw new ArgumentNullException(nameof(schemes));
        _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
    }

    public async Task<AccountOpenedResult> OpenAsync(
        OpenAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.InstitutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ApplicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        if (_tenantContext.CurrentInstitution != command.InstitutionId)
        {
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the application target.");
        }

        string applicationId = command.ApplicationId.Trim();
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|', applicationId, command.ExpectedVersion));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.open", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => OpenCoreAsync(command, applicationId, token),
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(outcome.Result))
        {
            throw new InvalidOperationException("The account opening did not produce an idempotent result.");
        }

        AccountOpenedResult result = System.Text.Json.JsonSerializer.Deserialize<AccountOpenedResult>(outcome.Result)
            ?? throw new InvalidOperationException("The stored account opening evidence is not readable.");
        return result with { Replayed = outcome.Kind == IdempotencyOutcomeKind.Replayed };
    }

    private async Task<string?> OpenCoreAsync(
        OpenAccountCommand command,
        string applicationId,
        CancellationToken cancellationToken)
    {
        AccountApplicationRecord application = await _context.AccountApplications
            .SingleOrDefaultAsync(record => record.AccountApplicationId == applicationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_application_not_found", "The account application is not available in this institution.");

        AccountRecord? existing = await _context.Accounts
            .SingleOrDefaultAsync(record => record.AccountApplicationId == applicationId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Serialise(existing, replayed: false);
        }

        if (command.ExpectedVersion != application.Version)
        {
            throw Reject("account_application_version_mismatch", "The application version does not match the submitted expectation.");
        }

        AccountApplicationStatus nextStatus;
        try
        {
            nextStatus = AccountApplicationLifecycle.RecordOpened(ParseStatus(application.Status));
        }
        catch (InvalidOperationException exception)
        {
            throw Reject(
                "account_application_not_approved",
                "Only an approved application can produce an account.",
                exception);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        AccountNumberSchemeLookup scheme = await _schemes
            .GetAsync(command.InstitutionId, now, cancellationToken)
            .ConfigureAwait(false);
        if (scheme.Scheme is null || string.IsNullOrWhiteSpace(scheme.VersionId))
        {
            throw Reject(
                scheme.RejectionCode ?? "account_number_scheme_missing",
                "The institution account-number scheme is unavailable.");
        }

        string branchCode = await ResolveBranchCodeAsync(application.BranchId, cancellationToken).ConfigureAwait(false);

        // The reservation, the account row, the history, the audit and the outbox fact commit together.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        AllocatedAccountNumber allocated;
        try
        {
            allocated = await _allocator
                .AllocateAsync(
                    command.InstitutionId,
                    scheme.Scheme,
                    scheme.VersionId,
                    application.ProductCode,
                    branchCode,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            // A scheme that cannot project the approved codes must never produce a partial number.
            throw Reject("account_number_segment_invalid", exception.Message, exception);
        }

        string accountId = Guid.NewGuid().ToString("N");
        AccountRecord account = new()
        {
            InstitutionId = command.InstitutionId.Value,
            AccountId = accountId,
            AccountApplicationId = application.AccountApplicationId,
            CustomerId = application.CustomerId,
            ProductId = application.ProductId,
            ProductCode = application.ProductCode,
            ProductVersionId = application.ProductVersionId,
            ProductContentHash = application.ProductContentHash,
            BranchId = application.BranchId,
            CurrencyCode = application.CurrencyCode,
            AccountPurposeCode = application.AccountPurposeCode,
            OwnershipTypeCode = application.OwnershipTypeCode,
            AccountNumber = allocated.AccountNumber,
            MaskedAccountNumber = allocated.MaskedAccountNumber,
            SchemeId = allocated.SchemeId,
            SchemeVersionId = allocated.SchemeVersionId,
            Status = nameof(AccountApplicationStatus.Open),
            LedgerInitialisationStatus = NotInitialised,
            Version = 1,
            OpenedAt = now,
        };
        _context.Accounts.Add(account);
        _context.AccountStatusHistory.Add(new AccountStatusHistoryRecord
        {
            InstitutionId = command.InstitutionId.Value,
            HistoryId = Guid.NewGuid().ToString("N"),
            AccountId = accountId,
            Status = account.Status,
            ReasonCode = "account_opened",
            ActorId = command.ActorId.Value,
            OccurredAt = now,
        });

        application.Status = nextStatus.ToString();
        application.Version++;
        application.UpdatedAt = now;

        AddAudit(command, accountId, allocated.MaskedAccountNumber, now);
        AddOutboxFact(command, account, now);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Serialise(account, replayed: false);
    }

    private void AddOutboxFact(OpenAccountCommand command, AccountRecord account, DateTimeOffset now)
    {
        string eventId = Guid.NewGuid().ToString("N");
        AccountOpenedV1 payload = new(
            account.InstitutionId,
            account.AccountId,
            account.MaskedAccountNumber,
            account.CustomerId,
            account.ProductId,
            account.ProductVersionId,
            account.BranchId,
            account.CurrencyCode,
            account.Status,
            account.Version,
            account.OpenedAt);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = AccountOpenedEventType,
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
            EventType = AccountOpenedEventType,
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

    private void AddAudit(OpenAccountCommand command, string accountId, string maskedAccountNumber, DateTimeOffset now)
    {
        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = command.InstitutionId.Value,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = command.ActorId.Value,
            ActorType = "human",
            Permission = AccountPermissions.ApplicationApproveRequirement.Permission.Value,
            Action = "account.open",
            ResourceType = "account.account",
            ResourceId = accountId,
            Outcome = "succeeded",
            Reason = string.Empty,
            CorrelationId = command.CorrelationId,
            AfterReference = maskedAccountNumber,
            OccurredAt = now,
        });
    }

    /// <summary>
    /// The scheme projects approved code lists, so the stored branch code is used, never the internal id.
    /// </summary>
    private async Task<string> ResolveBranchCodeAsync(string branchId, CancellationToken cancellationToken)
    {
        BranchRecord? branch = await _context.Branches
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.BranchId == branchId, cancellationToken)
            .ConfigureAwait(false);
        return branch?.Code ?? throw Reject("branch_not_available", "The application branch is not available.");
    }

    private static AccountApplicationStatus ParseStatus(string status) =>
        Enum.TryParse(status, ignoreCase: false, out AccountApplicationStatus parsed)
            ? parsed
            : throw new InvalidOperationException($"account_application_status_unknown:{status}");

    private static string Serialise(AccountRecord account, bool replayed) =>
        System.Text.Json.JsonSerializer.Serialize(new AccountOpenedResult(
            account.AccountId,
            account.AccountApplicationId,
            account.MaskedAccountNumber,
            account.CustomerId,
            account.ProductId,
            account.ProductVersionId,
            account.BranchId,
            account.CurrencyCode,
            account.Status,
            account.LedgerInitialisationStatus,
            account.Version,
            account.OpenedAt,
            replayed));

    private static AccountApplicationRejectedException Reject(string code, string message) => new(code, message);

    private static AccountApplicationRejectedException Reject(string code, string message, Exception inner) =>
        new(code, message, inner);
}
