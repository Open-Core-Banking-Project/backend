using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
/// Owns the approved closure ordering: a proposal records the ledger's condition snapshot without changing the
/// account status, submission binds approval to that exact snapshot hash, approval moves the account to pending
/// closure, and a final re-check against a fresh ledger answer is what closes it. The module records the ledger's
/// numbers and never forces a balance to zero. Decision: P1-E04-D13.
/// </summary>
public sealed class AccountClosureService : IAccountClosureService, IAccountClosureApprovalApplier
{
    private const string Producer = "core-ledger";
    private const string AccountEventFamily = "account";
    private const string AccountClosedEventType = "account_closed";
    private const string StatusChangedEventType = "account_status_changed";
    private const int AccountSchemaVersion = 1;
    private static readonly TimeSpan FreshnessCeiling = TimeSpan.FromSeconds(60);

    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILedgerClosureConditionSource _conditions;

    public AccountClosureService(
        CoreLedgerDbContext context,
        IdempotencyService idempotency,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        ILedgerClosureConditionSource conditions)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
    }

    public async Task<AccountClosureProposalResult> ProposeAsync(
        CreateAccountClosureProposalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.AccountId, command.CorrelationId);
        if (string.IsNullOrWhiteSpace(command.ReasonCode))
        {
            throw Reject("account_closure_reason_required", "A closure proposal requires a reason code.");
        }

        if (string.IsNullOrWhiteSpace(command.RequestId))
        {
            throw Reject("account_closure_request_required", "A closure proposal requires a request identifier.");
        }

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            command.AccountId.Trim(),
            command.RequestId.Trim(),
            command.ReasonCode,
            command.SupportingReference ?? string.Empty,
            command.LienReleaseDecisionRecorded,
            command.DisputeOpen,
            command.NoticePending));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.closure.propose", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => ProposeCoreAsync(command, token),
            cancellationToken).ConfigureAwait(false);
        return Decode(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountClosureProposalResult> SubmitForApprovalAsync(
        SubmitAccountClosureProposalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.AccountId, command.CorrelationId);
        if (string.IsNullOrWhiteSpace(command.RequestId) || string.IsNullOrWhiteSpace(command.SourceRequestKey))
        {
            throw Reject("account_closure_request_required", "A closure submission requires a request and source request key.");
        }

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            command.ProposalId.Trim(),
            command.ExpectedVersion,
            command.RequestId.Trim(),
            command.SourceRequestKey.Trim()));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.closure.submit", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => SubmitCoreAsync(command, token),
            cancellationToken).ConfigureAwait(false);
        return Decode(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountClosureProposalResult> RecheckAsync(
        RecheckAccountClosureCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.AccountId, command.CorrelationId);

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            command.ProposalId.Trim(),
            command.ExpectedVersion));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.closure.recheck", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => RecheckCoreAsync(command, token),
            cancellationToken).ConfigureAwait(false);
        return Decode(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountClosureProposalResult> GetAsync(
        InstitutionId institutionId,
        string accountId,
        string proposalId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        if (_tenantContext.CurrentInstitution != institutionId)
        {
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the account target.");
        }

        AccountClosureProposalRecord record = await _context.AccountClosureProposals
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.ProposalId == proposalId.Trim() && candidate.AccountId == accountId.Trim(),
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_closure_proposal_not_found", "The closure proposal is not available for this account.");
        return ToResult(institutionId.Value, record, replayed: false);
    }

    /// <summary>
    /// Applies the Workflow decision inside the consumer's transaction. An approval moves the account to pending
    /// closure and then runs the final condition re-check; only a settled re-check closes it. A rejection leaves
    /// the account untouched. Decision: P1-E04-D13.
    /// </summary>
    public async Task<string> ApplyDecisionAsync(
        InstitutionId institutionId,
        string proposalId,
        string requestId,
        string subjectVersion,
        string subjectHash,
        bool approved,
        string approverActorId,
        DateTimeOffset decidedAt,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        AccountClosureProposalRecord proposal = await _context.AccountClosureProposals
            .SingleOrDefaultAsync(record => record.ProposalId == proposalId.Trim(), cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The submitted closure proposal is not available in this institution.");

        if (!string.Equals(proposal.ApprovalRequestId, requestId, StringComparison.Ordinal)
            || !string.Equals(proposal.ConditionSnapshotHash, subjectHash, StringComparison.Ordinal)
            || !string.Equals(proposal.Version.ToString(CultureInfo.InvariantCulture), subjectVersion, StringComparison.Ordinal)
            || !string.Equals(proposal.Status, StatusToken(AccountClosureProposalStatus.PendingApproval), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The closure decision does not match the exact submitted proposal version and condition snapshot.");
        }

        AccountRecord account = await _context.Accounts
            .SingleAsync(record => record.AccountId == proposal.AccountId, cancellationToken)
            .ConfigureAwait(false);

        if (!approved)
        {
            proposal.Status = StatusToken(AccountClosureProposalStatus.Rejected);
            proposal.Version++;
            proposal.UpdatedAt = decidedAt;
            AddProposalAudit(institutionId.Value, account.AccountId, proposal.ProposalId, "account.closure.reject", ["account_closure_rejected"], correlationId, decidedAt, approverActorId, "human");
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return "rejected:" + proposal.ProposalId;
        }

        proposal.ApprovedByActorId = approverActorId;
        proposal.ApprovedAt = decidedAt;
        proposal.Status = StatusToken(AccountClosureProposalStatus.PendingClosure);
        proposal.Version++;
        proposal.UpdatedAt = decidedAt;

        AccountStatus current = AccountLifecycle.Parse(account.Status);
        string previous = account.Status;
        long controlVersion = await CurrentControlVersionAsync(account.AccountId, cancellationToken).ConfigureAwait(false);
        account.Status = AccountLifecycle.Token(AccountLifecycle.ProposeClosure(current));
        account.Version++;
        AppendHistory(institutionId.Value, account.AccountId, account.Status, "account_closure_approved", approverActorId, decidedAt);
        AddStatusChangedFact(institutionId.Value, account, previous, "account_closure_approved", controlVersion, correlationId, decidedAt);
        AddProposalAudit(institutionId.Value, account.AccountId, proposal.ProposalId, "account.closure.approve", ["account_closure_approved"], correlationId, decidedAt, approverActorId, "human");

        bool closed = await TryCloseAsync(institutionId, account, proposal, approverActorId, decidedAt, correlationId, cancellationToken)
            .ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (closed ? "approved:" : "approved_recheck_required:") + proposal.ProposalId;
    }

    private async Task<string?> ProposeCoreAsync(
        CreateAccountClosureProposalCommand command,
        CancellationToken cancellationToken)
    {
        AccountRecord account = await LoadAccountAsync(command.AccountId, cancellationToken).ConfigureAwait(false);
        AccountStatus status = AccountLifecycle.Parse(account.Status);
        if (status is AccountStatus.PendingClosure or AccountStatus.Closed)
        {
            throw Reject(AccountClosurePolicy.ProposalConditionsNotMetCode, "The account is already closing or closed.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        (LedgerClosureConditions conditions, AccountClosureConditionResult evaluation) = await EvaluateAsync(
            command.InstitutionId,
            account,
            new AccountClosureFacts(
                LienReleaseDecisionRecorded: command.LienReleaseDecisionRecorded,
                DisputeOpen: command.DisputeOpen,
                NoticePending: command.NoticePending,
                LegalControlActive: await HasActiveLegalControlAsync(account.AccountId, now, cancellationToken).ConfigureAwait(false),
                StatutoryOrTaxLienOutstanding: 0m,
                InstitutionOrCustomerLienOutstanding: 0m),
            now,
            cancellationToken).ConfigureAwait(false);

        AccountClosureProposalRecord record = new()
        {
            InstitutionId = command.InstitutionId.Value,
            ProposalId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            RequestId = command.RequestId.Trim(),
            ReasonCode = command.ReasonCode.Trim(),
            SupportingReference = command.SupportingReference?.Trim(),
            Status = StatusToken(evaluation.Settled
                ? AccountClosureProposalStatus.ConditionsMet
                : AccountClosureProposalStatus.ConditionsFailed),
            ConditionSnapshotHash = evaluation.SnapshotHash,
            LedgerAccountReference = conditions.LedgerAccountReference,
            LedgerSnapshotVersion = conditions.SnapshotVersion,
            ConditionsAsOf = conditions.AsOf,
            CurrencyCode = conditions.CurrencyCode,
            LedgerBalance = conditions.LedgerBalance,
            AvailableBalance = conditions.AvailableBalance,
            Holds = conditions.Holds,
            OutstandingLienAmount = conditions.OutstandingLienAmount + await OutstandingLienAsync(account.AccountId, now, cancellationToken).ConfigureAwait(false),
            PendingSettlements = conditions.PendingSettlements,
            LienReleaseDecisionRecorded = command.LienReleaseDecisionRecorded,
            DisputeOpen = command.DisputeOpen,
            NoticePending = command.NoticePending,
            RefusalCodes = string.Join(',', evaluation.RefusalCodes),
            CreatedByActorId = command.ActorId.Value,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _context.AccountClosureProposals.Add(record);
        AddProposalAudit(command.InstitutionId.Value, account.AccountId, record.ProposalId, "account.closure.propose", evaluation.RefusalCodes, command.CorrelationId, now);
        await SaveAsync("account_closure_proposal_conflict", "The closure proposal changed while it was being recorded.", cancellationToken)
            .ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Serialize(ToResult(command.InstitutionId.Value, record, replayed: false));
    }

    private async Task<string?> SubmitCoreAsync(
        SubmitAccountClosureProposalCommand command,
        CancellationToken cancellationToken)
    {
        AccountClosureProposalRecord proposal = await LoadProposalAsync(command.AccountId, command.ProposalId, cancellationToken)
            .ConfigureAwait(false);
        if (proposal.Version != command.ExpectedVersion)
        {
            throw Reject("account_closure_proposal_version_mismatch", "The proposal version does not match the submitted expectation.");
        }

        if (!string.Equals(proposal.Status, StatusToken(AccountClosureProposalStatus.ConditionsMet), StringComparison.Ordinal))
        {
            throw Reject(AccountClosurePolicy.ProposalConditionsNotMetCode, "Only a proposal whose conditions were met can be submitted.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        proposal.ApprovalRequestId = command.RequestId.Trim();
        proposal.Status = StatusToken(AccountClosureProposalStatus.PendingApproval);
        proposal.Version++;
        proposal.UpdatedAt = now;
        string eventId = AddApprovalRequested(
            proposal,
            command.RequestId.Trim(),
            command.SourceRequestKey.Trim(),
            command.ActorId.Value,
            command.CorrelationId,
            now);
        AddProposalAudit(command.InstitutionId.Value, proposal.AccountId, proposal.ProposalId, "account.closure.submit", [eventId], command.CorrelationId, now);
        await SaveAsync("account_closure_submission_conflict", "The proposal changed while approval was being submitted.", cancellationToken)
            .ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Serialize(ToResult(command.InstitutionId.Value, proposal, replayed: false));
    }

    private async Task<string?> RecheckCoreAsync(
        RecheckAccountClosureCommand command,
        CancellationToken cancellationToken)
    {
        AccountClosureProposalRecord proposal = await LoadProposalAsync(command.AccountId, command.ProposalId, cancellationToken)
            .ConfigureAwait(false);
        if (proposal.Version != command.ExpectedVersion)
        {
            throw Reject("account_closure_proposal_version_mismatch", "The proposal version does not match the submitted expectation.");
        }

        if (string.Equals(proposal.Status, StatusToken(AccountClosureProposalStatus.Closed), StringComparison.Ordinal))
        {
            return System.Text.Json.JsonSerializer.Serialize(ToResult(command.InstitutionId.Value, proposal, replayed: true));
        }

        if (!string.Equals(proposal.Status, StatusToken(AccountClosureProposalStatus.PendingClosure), StringComparison.Ordinal))
        {
            throw Reject(AccountClosurePolicy.RecheckRequiredCode, "Only a proposal pending closure can be re-checked.");
        }

        AccountRecord account = await _context.Accounts
            .SingleAsync(record => record.AccountId == proposal.AccountId, cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        await TryCloseAsync(
            command.InstitutionId, account, proposal, command.ActorId.Value, now, command.CorrelationId, cancellationToken)
            .ConfigureAwait(false);
        await SaveAsync("account_closure_recheck_conflict", "The closure proposal changed while the re-check ran.", cancellationToken)
            .ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Serialize(ToResult(command.InstitutionId.Value, proposal, replayed: false));
    }

    /// <summary>
    /// The final re-check: the fresh ledger answer must still be settled, otherwise the account stays pending
    /// closure and the refusal codes are recorded so a human can act. Decision: P1-E04-D13.
    /// </summary>
    private async Task<bool> TryCloseAsync(
        InstitutionId institutionId,
        AccountRecord account,
        AccountClosureProposalRecord proposal,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        (_, AccountClosureConditionResult evaluation) = await EvaluateAsync(
            institutionId,
            account,
            new AccountClosureFacts(
                LienReleaseDecisionRecorded: proposal.LienReleaseDecisionRecorded,
                DisputeOpen: proposal.DisputeOpen,
                NoticePending: proposal.NoticePending,
                LegalControlActive: await HasActiveLegalControlAsync(account.AccountId, now, cancellationToken).ConfigureAwait(false),
                StatutoryOrTaxLienOutstanding: 0m,
                InstitutionOrCustomerLienOutstanding: 0m),
            now,
            cancellationToken).ConfigureAwait(false);

        proposal.RefusalCodes = string.Join(',', evaluation.RefusalCodes);
        proposal.UpdatedAt = now;
        if (!evaluation.Settled)
        {
            proposal.Version++;
            return false;
        }

        long controlVersion = await CurrentControlVersionAsync(account.AccountId, cancellationToken).ConfigureAwait(false);
        string previous = account.Status;
        account.Status = AccountLifecycle.Token(AccountLifecycle.Close(AccountLifecycle.Parse(account.Status)));
        account.Version++;
        proposal.Status = StatusToken(AccountClosureProposalStatus.Closed);
        proposal.ClosedAt = now;
        proposal.Version++;
        AppendHistory(institutionId.Value, account.AccountId, account.Status, "account_closed", actorId, now);
        AddStatusChangedFact(institutionId.Value, account, previous, "account_closed", controlVersion, correlationId, now);
        AddClosedFact(institutionId.Value, account, proposal, correlationId, now);
        AddProposalAudit(institutionId.Value, account.AccountId, proposal.ProposalId, "account.close", [], correlationId, now);
        return true;
    }

    private async Task<(LedgerClosureConditions Conditions, AccountClosureConditionResult Evaluation)> EvaluateAsync(
        InstitutionId institutionId,
        AccountRecord account,
        AccountClosureFacts facts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        LedgerClosureConditions conditions = await _conditions
            .GetAsync(institutionId, account.AccountId, account.CurrencyCode, cancellationToken)
            .ConfigureAwait(false);
        AccountClosureFacts combined = facts with
        {
            StatutoryOrTaxLienOutstanding = await StatutoryOrTaxLienAsync(account.AccountId, now, cancellationToken).ConfigureAwait(false),
            InstitutionOrCustomerLienOutstanding = await InstitutionOrCustomerLienAsync(account.AccountId, now, cancellationToken).ConfigureAwait(false),
        };
        return (conditions, AccountClosurePolicy.Evaluate(conditions, combined, now, FreshnessCeiling));
    }

    private async Task<decimal> StatutoryOrTaxLienAsync(string accountId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await SumLienAsync(accountId, now, ["statutory_first", "tax"], cancellationToken).ConfigureAwait(false);

    private async Task<decimal> InstitutionOrCustomerLienAsync(string accountId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await SumLienAsync(accountId, now, ["institution", "customer"], cancellationToken).ConfigureAwait(false);

    private async Task<decimal> OutstandingLienAsync(string accountId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await SumLienAsync(accountId, now, ["statutory_first", "tax", "institution", "customer"], cancellationToken).ConfigureAwait(false);

    private async Task<decimal> SumLienAsync(
        string accountId,
        DateTimeOffset now,
        string[] priorityCodes,
        CancellationToken cancellationToken)
    {
        decimal[] outstanding = await _context.AccountLiens
            .AsNoTracking()
            .Where(record => record.AccountId == accountId
                && record.Status == "active"
                && priorityCodes.Contains(record.PriorityCode)
                && record.EffectiveFrom <= now
                && (record.EffectiveTo == null || now < record.EffectiveTo))
            .Select(record => record.OutstandingAmount)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return outstanding.Sum();
    }

    private async Task<bool> HasActiveLegalControlAsync(
        string accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool legalBlock = await _context.AccountBlocks
            .AsNoTracking()
            .AnyAsync(
                record => record.AccountId == accountId
                    && record.Status == "active"
                    && record.BlockTypeCode == "legal"
                    && record.EffectiveFrom <= now
                    && (record.EffectiveTo == null || now < record.EffectiveTo),
                cancellationToken)
            .ConfigureAwait(false);
        if (legalBlock)
        {
            return true;
        }

        return await _context.AccountFreezes
            .AsNoTracking()
            .AnyAsync(
                record => record.AccountId == accountId
                    && record.Status == "active"
                    && record.FreezeTypeCode == "regulatory"
                    && record.EffectiveFrom <= now
                    && (record.EffectiveTo == null || now < record.EffectiveTo),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private string AddApprovalRequested(
        AccountClosureProposalRecord proposal,
        string requestId,
        string sourceRequestKey,
        string actorId,
        string correlationId,
        DateTimeOffset now)
    {
        string eventId = Guid.NewGuid().ToString("N");
        ApprovalRequestedV1 payload = new(
            proposal.InstitutionId,
            requestId,
            sourceRequestKey,
            AccountClosureApprovalV1.WorkflowKey,
            AccountClosureApprovalV1.WorkflowVersionId,
            AccountClosureApprovalV1.WorkflowDefinitionHash,
            AccountClosureApprovalV1.SourceService,
            AccountClosureApprovalV1.SubjectType,
            proposal.ProposalId,
            proposal.Version.ToString(CultureInfo.InvariantCulture),
            proposal.ConditionSnapshotHash,
            actorId,
            AccountClosureApprovalV1.RequiredPermission,
            correlationId,
            now);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = "approval_requested",
            SchemaVersion = 1,
            OccurredAt = now,
            InstitutionId = proposal.InstitutionId,
            CorrelationId = correlationId,
            Producer = Producer,
            BusinessReference = requestId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = proposal.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = "approval_requested",
            SchemaVersion = 1,
            Topic = EventContract.TopicFor(Producer, "approval", 1),
            BusinessReference = requestId,
            CorrelationId = correlationId,
            TraceParent = System.Diagnostics.Activity.Current?.Id,
            TraceState = System.Diagnostics.Activity.Current?.TraceStateString,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });
        return eventId;
    }

    private void AddClosedFact(
        string institutionId,
        AccountRecord account,
        AccountClosureProposalRecord proposal,
        string correlationId,
        DateTimeOffset now)
    {
        string eventId = Guid.NewGuid().ToString("N");
        AccountClosedV1 payload = new(
            institutionId,
            account.AccountId,
            proposal.ProposalId,
            proposal.LedgerAccountReference,
            proposal.LedgerSnapshotVersion,
            account.Version,
            now);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = AccountClosedEventType,
            SchemaVersion = AccountSchemaVersion,
            OccurredAt = now,
            InstitutionId = institutionId,
            CorrelationId = correlationId,
            Producer = Producer,
            BusinessReference = account.AccountId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = institutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = AccountClosedEventType,
            SchemaVersion = AccountSchemaVersion,
            Topic = EventContract.TopicFor(Producer, AccountEventFamily, AccountSchemaVersion),
            BusinessReference = account.AccountId,
            CorrelationId = correlationId,
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

    private void AddStatusChangedFact(
        string institutionId,
        AccountRecord account,
        string previousStatus,
        string reasonCode,
        long controlVersion,
        string correlationId,
        DateTimeOffset now)
    {
        string eventId = Guid.NewGuid().ToString("N");
        AccountStatusChangedV1 payload = new(
            institutionId,
            account.AccountId,
            previousStatus,
            account.Status,
            reasonCode,
            controlVersion,
            account.Version,
            now);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = StatusChangedEventType,
            SchemaVersion = AccountSchemaVersion,
            OccurredAt = now,
            InstitutionId = institutionId,
            CorrelationId = correlationId,
            Producer = Producer,
            BusinessReference = account.AccountId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = institutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = StatusChangedEventType,
            SchemaVersion = AccountSchemaVersion,
            Topic = EventContract.TopicFor(Producer, AccountEventFamily, AccountSchemaVersion),
            BusinessReference = account.AccountId,
            CorrelationId = correlationId,
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

    private void AppendHistory(
        string institutionId,
        string accountId,
        string status,
        string reasonCode,
        string actorId,
        DateTimeOffset now) =>
        _context.AccountStatusHistory.Add(new AccountStatusHistoryRecord
        {
            InstitutionId = institutionId,
            HistoryId = Guid.NewGuid().ToString("N"),
            AccountId = accountId,
            Status = status,
            ReasonCode = reasonCode,
            ActorId = actorId,
            OccurredAt = now,
        });

    private void AddProposalAudit(
        string institutionId,
        string accountId,
        string proposalId,
        string action,
        IEnumerable<string> evidence,
        string correlationId,
        DateTimeOffset now,
        string actorId = "service",
        string actorType = "service") =>
        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = institutionId,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = actorId,
            ActorType = actorType,
            Permission = AccountClosureApprovalV1.RequiredPermission,
            Action = action,
            ResourceType = "account.closure_proposal",
            ResourceId = proposalId,
            Outcome = "succeeded",
            Reason = string.Join(',', evidence),
            CorrelationId = correlationId,
            AfterReference = accountId,
            OccurredAt = now,
        });

    private async Task<long> CurrentControlVersionAsync(string accountId, CancellationToken cancellationToken)
    {
        long restriction = await _context.AccountRestrictions
            .AsNoTracking()
            .Where(record => record.AccountId == accountId)
            .Select(record => record.ControlVersion)
            .DefaultIfEmpty(0)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);
        long freeze = await _context.AccountFreezes
            .AsNoTracking()
            .Where(record => record.AccountId == accountId)
            .Select(record => record.ControlVersion)
            .DefaultIfEmpty(0)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);
        long block = await _context.AccountBlocks
            .AsNoTracking()
            .Where(record => record.AccountId == accountId)
            .Select(record => record.ControlVersion)
            .DefaultIfEmpty(0)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);
        return Math.Max(restriction, Math.Max(freeze, block));
    }

    private async Task<AccountRecord> LoadAccountAsync(string accountId, CancellationToken cancellationToken) =>
        await _context.Accounts
            .SingleOrDefaultAsync(record => record.AccountId == accountId.Trim(), cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_not_found", "The account is not available in this institution.");

    private async Task<AccountClosureProposalRecord> LoadProposalAsync(
        string accountId,
        string proposalId,
        CancellationToken cancellationToken) =>
        await _context.AccountClosureProposals
            .SingleOrDefaultAsync(
                record => record.ProposalId == proposalId.Trim() && record.AccountId == accountId.Trim(),
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_closure_proposal_not_found", "The closure proposal is not available for this account.");

    private static string StatusToken(AccountClosureProposalStatus status) => status switch
    {
        AccountClosureProposalStatus.ConditionsMet => "conditions_met",
        AccountClosureProposalStatus.ConditionsFailed => "conditions_failed",
        AccountClosureProposalStatus.PendingApproval => "pending_approval",
        AccountClosureProposalStatus.PendingClosure => "pending_closure",
        AccountClosureProposalStatus.Closed => "closed",
        AccountClosureProposalStatus.Rejected => "rejected",
        _ => throw new ArgumentException("The closure proposal status is not approved.", nameof(status)),
    };

    private static AccountClosureProposalResult ToResult(
        string institutionId,
        AccountClosureProposalRecord record,
        bool replayed) =>
        new(
            institutionId,
            record.ProposalId,
            record.AccountId,
            record.RequestId,
            record.Status,
            record.ReasonCode,
            record.SupportingReference,
            record.RefusalCodes.Length == 0 ? [] : record.RefusalCodes.Split(','),
            record.ConditionSnapshotHash,
            record.LedgerAccountReference,
            record.LedgerSnapshotVersion,
            record.ConditionsAsOf,
            record.LedgerBalance,
            record.AvailableBalance,
            record.Holds,
            record.OutstandingLienAmount,
            record.PendingSettlements,
            record.ApprovalRequestId,
            record.UpdatedAt,
            record.Version,
            replayed);

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

    private void ValidateTarget(
        InstitutionId institutionId,
        string idempotencyKey,
        string accountId,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (_tenantContext.CurrentInstitution != institutionId)
        {
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the account target.");
        }
    }

    private static AccountClosureProposalResult Decode(IdempotencyOutcome outcome, bool replayed)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (string.IsNullOrWhiteSpace(outcome.Result))
        {
            throw new InvalidOperationException("The closure command did not produce an idempotent result.");
        }

        AccountClosureProposalResult? decoded = System.Text.Json.JsonSerializer.Deserialize<AccountClosureProposalResult>(outcome.Result);
        return decoded is null
            ? throw new InvalidOperationException("The stored closure evidence is not readable.")
            : decoded with { Replayed = replayed };
    }

    private static AccountApplicationRejectedException Reject(string code, string message) => new(code, message);
}
