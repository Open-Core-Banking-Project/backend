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
/// Runs the approved dormancy assessment and reactivation. The assessment is a pure decision over the ledger's
/// posted history, the institution business date and the effective rule version; it records every outcome,
/// including "not dormant" and "not assessable", and only a permitted transition changes the account status.
/// Reactivation clears dormancy alone: freezes, blocks and liens survive it. Decision: P1-E04-D12.
/// </summary>
public sealed class AccountDormancyService : IAccountDormancyService
{
    private const string Producer = "core-ledger";
    private const string AccountEventFamily = "account";
    private const string StatusChangedEventType = "account_status_changed";
    private const int AccountSchemaVersion = 1;
    private const string UnpublishedRuleVersion = "unpublished";
    private const string NoticeRecordedStatus = "recorded";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly IDormancyRuleSource _rules;
    private readonly IBusinessDateSource _businessDates;
    private readonly IAccountActivitySource _activity;

    public AccountDormancyService(
        CoreLedgerDbContext context,
        IdempotencyService idempotency,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        IDormancyRuleSource rules,
        IBusinessDateSource businessDates,
        IAccountActivitySource activity)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _businessDates = businessDates ?? throw new ArgumentNullException(nameof(businessDates));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
    }

    public async Task<DormancyAssessmentResult> AssessAsync(
        AssessAccountDormancyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.AccountId, command.CorrelationId);

        string accountId = command.AccountId.Trim();
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            accountId,
            command.BusinessDate?.ToString("O", CultureInfo.InvariantCulture) ?? "current"));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.dormancy.assess", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => AssessCoreAsync(command, accountId, token),
            cancellationToken).ConfigureAwait(false);
        return Decode(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<IReadOnlyList<DormancyAssessmentResult>> AssessCatchUpAsync(
        InstitutionId institutionId,
        ActorId actorId,
        string accountId,
        DateOnly? fromBusinessDate,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        DateOnly current = await _businessDates
            .GetCurrentBusinessDateAsync(institutionId, cancellationToken)
            .ConfigureAwait(false);

        AccountRecord account = await LoadAccountAsync(institutionId, accountId, cancellationToken).ConfigureAwait(false);
        DateOnly origin = fromBusinessDate ?? DateOnly.FromDateTime(account.OpenedAt.UtcDateTime);
        if (origin > current)
        {
            return [];
        }

        List<DateOnly> dates = [];
        for (DateOnly date = origin; date <= current; date = date.AddDays(1))
        {
            dates.Add(date);
        }

        DateOnly[] alreadyAssessed = [.. (await _context.AccountDormancyAssessments
            .AsNoTracking()
            .Where(record => record.AccountId == account.AccountId)
            .Select(record => record.BusinessDate)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
            .Distinct()];
        List<DormancyAssessmentResult> results = [];
        foreach (DateOnly date in AccountDormancyPolicy.OrderForCatchUp(dates))
        {
            if (alreadyAssessed.Contains(date))
            {
                continue;
            }

            results.Add(await AssessAsync(
                new AssessAccountDormancyCommand(
                    institutionId,
                    actorId,
                    $"catchup:{account.AccountId}:{date:yyyyMMdd}",
                    account.AccountId,
                    date,
                    correlationId),
                cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    public async Task<AccountReactivatedResult> ReactivateAsync(
        ReactivateAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.AccountId, command.CorrelationId);
        if (string.IsNullOrWhiteSpace(command.ReasonCode))
        {
            throw Reject("account_reactivation_reason_required", "A reactivation requires a reason code.");
        }

        string accountId = command.AccountId.Trim();
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            accountId,
            command.ExpectedVersion,
            command.ReasonCode,
            command.ApprovalReference ?? string.Empty));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.dormancy.reactivate", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => ReactivateCoreAsync(command, accountId, token),
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(outcome.Result))
        {
            throw new InvalidOperationException("The reactivation did not produce an idempotent result.");
        }

        AccountReactivatedResult result = System.Text.Json.JsonSerializer.Deserialize<AccountReactivatedResult>(outcome.Result)
            ?? throw new InvalidOperationException("The stored reactivation evidence is not readable.");
        return result with { Replayed = outcome.Kind == IdempotencyOutcomeKind.Replayed };
    }

    private async Task<string?> ReactivateCoreAsync(
        ReactivateAccountCommand command,
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

        AccountStatus current = AccountLifecycle.Parse(account.Status);
        if (current == AccountStatus.Active)
        {
            return SerialiseReactivation(account, command.ReasonCode, _timeProvider.GetUtcNow(), replayed: true);
        }

        if (current != AccountStatus.Dormant)
        {
            throw Reject(AccountDormancyPolicy.ReactivationNotDormantCode, "Only a dormant account can be reactivated.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        DormancyRuleLookup lookup = await _rules.GetAsync(command.InstitutionId, now, cancellationToken).ConfigureAwait(false);
        if (lookup.Rule is null)
        {
            throw Reject(lookup.RejectionCode ?? AccountDormancyPolicy.RuleMissingCode, "The institution dormancy rule is unavailable.");
        }

        string maker = command.MakerActorId?.Trim() ?? string.Empty;
        IReadOnlyList<string> refusals = AccountDormancyPolicy.ReactivationRefusals(
            lookup.Rule,
            !string.IsNullOrWhiteSpace(command.ApprovalReference),
            string.Equals(maker, command.ActorId.Value, StringComparison.Ordinal));
        if (refusals.Count > 0)
        {
            throw Reject(refusals[0], "The reactivation does not satisfy the approved reactivation policy.");
        }

        long controlVersion = await CurrentControlVersionAsync(account.AccountId, cancellationToken).ConfigureAwait(false);
        string previous = account.Status;
        account.Status = AccountLifecycle.Token(AccountLifecycle.Reactivate(current));
        account.Version++;
        RecordStatusChange(command, account, previous, controlVersion, command.ReasonCode, now);
        await SaveAsync("account_reactivation_conflict", "The account changed while the reactivation was being recorded.", cancellationToken)
            .ConfigureAwait(false);
        return SerialiseReactivation(account, command.ReasonCode, now, replayed: false);
    }

    private async Task<string?> AssessCoreAsync(
        AssessAccountDormancyCommand command,
        string accountId,
        CancellationToken cancellationToken)
    {
        AccountRecord account = await LoadAccountAsync(command.InstitutionId, accountId, cancellationToken).ConfigureAwait(false);
        DateOnly currentBusinessDate = await _businessDates
            .GetCurrentBusinessDateAsync(command.InstitutionId, cancellationToken)
            .ConfigureAwait(false);
        DateOnly businessDate = command.BusinessDate ?? currentBusinessDate;
        if (businessDate > currentBusinessDate)
        {
            throw Reject("dormancy_business_date_in_future", "A dormancy assessment cannot run ahead of the institution business date.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        DormancyRuleLookup lookup = await _rules
            .GetAsync(command.InstitutionId, new DateTimeOffset(businessDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), cancellationToken)
            .ConfigureAwait(false);

        await AcquireLeaseAsync(command.InstitutionId, account.AccountId, now, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return await AssessWithinLeaseAsync(command, account, businessDate, now, lookup, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await ReleaseLeaseAsync(account.AccountId, now, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> AssessWithinLeaseAsync(
        AssessAccountDormancyCommand command,
        AccountRecord account,
        DateOnly businessDate,
        DateTimeOffset now,
        DormancyRuleLookup lookup,
        CancellationToken cancellationToken)
    {
        string ruleVersionId = lookup.VersionId ?? UnpublishedRuleVersion;
        string ruleContentHash = lookup.ContentHash ?? string.Empty;
        AccountDormancyAssessmentRecord? recorded = await _context.AccountDormancyAssessments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.AccountId == account.AccountId
                    && record.BusinessDate == businessDate
                    && record.RuleVersionId == ruleVersionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (recorded is not null)
        {
            // A repeated run returns the recorded outcome instead of assessing the same day twice.
            return SerialiseAssessment(ToResult(recorded, account, replayed: false));
        }

        if (lookup.Rule is null)
        {
            return await RecordNotAssessableAsync(
                command, account, businessDate, now, ruleVersionId, ruleContentHash,
                lookup.RejectionCode ?? AccountDormancyPolicy.RuleMissingCode, cancellationToken).ConfigureAwait(false);
        }

        AccountActivitySnapshot snapshot = await _activity
            .GetAsync(command.InstitutionId, account.AccountId, account.CurrencyCode, cancellationToken)
            .ConfigureAwait(false);
        if (!snapshot.Available)
        {
            return await RecordNotAssessableAsync(
                command, account, businessDate, now, ruleVersionId, ruleContentHash,
                snapshot.UnavailableCode ?? AccountDormancyPolicy.ActivitySourceUnavailableCode, cancellationToken).ConfigureAwait(false);
        }

        DormancyRule rule = lookup.Rule;
        DateOnly? lastQualifying = AccountDormancyPolicy.LastQualifyingActivity(snapshot.Movements, rule);
        bool noticeRecorded = await _context.AccountDormancyNotices
            .AsNoTracking()
            .AnyAsync(
                record => record.AccountId == account.AccountId
                    && record.RuleVersionId == ruleVersionId
                    && record.Status == NoticeRecordedStatus,
                cancellationToken)
            .ConfigureAwait(false);
        DateOnly origin = DateOnly.FromDateTime(account.OpenedAt.UtcDateTime);
        DormancyAssessmentOutcome outcome = AccountDormancyPolicy.Assess(
            businessDate, lastQualifying, origin, rule, noticeRecorded);

        AccountStatus current = AccountLifecycle.Parse(account.Status);
        bool transition = outcome.TransitionPermitted && current == AccountStatus.Active;
        string? previousStatus = transition ? account.Status : null;
        if (transition)
        {
            account.Status = AccountLifecycle.Token(AccountLifecycle.MarkDormant(current));
            account.Version++;
            long controlVersion = await CurrentControlVersionAsync(account.AccountId, cancellationToken).ConfigureAwait(false);
            RecordStatusChange(command, account, previousStatus!, controlVersion, "dormancy_threshold_reached", now);
        }

        AccountDormancyAssessmentRecord record = new()
        {
            InstitutionId = command.InstitutionId.Value,
            AssessmentId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            BusinessDate = businessDate,
            RuleVersionId = ruleVersionId,
            RuleContentHash = ruleContentHash,
            ThresholdDays = rule.ThresholdDays,
            NoticeLeadDays = rule.NoticeLeadDays,
            ClockOriginDate = origin,
            LastQualifyingActivityDate = outcome.LastQualifyingActivityDate,
            InactivityDays = outcome.InactivityDays,
            Decision = outcome.Decision.ToString(),
            ReasonCodes = string.Join(',', outcome.ReasonCodes),
            TransitionPermitted = outcome.TransitionPermitted,
            PreviousStatus = previousStatus,
            ResultingStatus = transition ? account.Status : null,
            AssessedAt = now,
        };
        _context.AccountDormancyAssessments.Add(record);
        AddAudit(command.InstitutionId, command.ActorId, "account.dormancy.assess", "account.dormancy.assess", account.AccountId, outcome.ReasonCodes, command.CorrelationId, now);
        await SaveAsync("dormancy_assessment_conflict", "The dormancy assessment changed while it was being recorded.", cancellationToken)
            .ConfigureAwait(false);
        return SerialiseAssessment(ToResult(record, account, replayed: false));
    }

    private async Task<string?> RecordNotAssessableAsync(
        AssessAccountDormancyCommand command,
        AccountRecord account,
        DateOnly businessDate,
        DateTimeOffset now,
        string ruleVersionId,
        string ruleContentHash,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        AccountDormancyAssessmentRecord record = new()
        {
            InstitutionId = command.InstitutionId.Value,
            AssessmentId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            BusinessDate = businessDate,
            RuleVersionId = ruleVersionId,
            RuleContentHash = ruleContentHash,
            ThresholdDays = 0,
            NoticeLeadDays = 0,
            ClockOriginDate = DateOnly.FromDateTime(account.OpenedAt.UtcDateTime),
            LastQualifyingActivityDate = null,
            InactivityDays = 0,
            Decision = DormancyDecision.NotAssessable.ToString(),
            ReasonCodes = reasonCode,
            TransitionPermitted = false,
            PreviousStatus = null,
            ResultingStatus = null,
            AssessedAt = now,
        };
        _context.AccountDormancyAssessments.Add(record);
        AddAudit(command.InstitutionId, command.ActorId, "account.dormancy.assess", "account.dormancy.assess", account.AccountId, [reasonCode], command.CorrelationId, now);
        await SaveAsync("dormancy_assessment_conflict", "The dormancy assessment changed while it was being recorded.", cancellationToken)
            .ConfigureAwait(false);
        return SerialiseAssessment(ToResult(record, account, replayed: false));
    }

    private async Task AcquireLeaseAsync(
        InstitutionId institutionId,
        string accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        AccountDormancyLeaseRecord? held = await _context.AccountDormancyLeases
            .FirstOrDefaultAsync(
                record => record.AccountId == accountId && record.ReleasedAt == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (held is not null)
        {
            if (held.ExpiresAt > now)
            {
                throw Reject("dormancy_assessment_lease_held", "Another assessment is already running for this account.");
            }

            // An expired lease is released so a crashed worker cannot block the account for ever.
            held.ReleasedAt = now;
        }

        _context.AccountDormancyLeases.Add(new AccountDormancyLeaseRecord
        {
            InstitutionId = institutionId.Value,
            LeaseId = Guid.NewGuid().ToString("N"),
            AccountId = accountId,
            AcquiredAt = now,
            ExpiresAt = now.Add(LeaseDuration),
            ReleasedAt = null,
        });
        await SaveAsync("dormancy_assessment_lease_held", "Another assessment is already running for this account.", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ReleaseLeaseAsync(string accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        AccountDormancyLeaseRecord? lease = await _context.AccountDormancyLeases
            .FirstOrDefaultAsync(record => record.AccountId == accountId && record.ReleasedAt == null, cancellationToken)
            .ConfigureAwait(false);
        if (lease is null)
        {
            return;
        }

        lease.ReleasedAt = now;
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // The assessment result is already recorded; a stale lease expires on its own.
        }
    }

    private void RecordStatusChange(
        AssessAccountDormancyCommand command,
        AccountRecord account,
        string previousStatus,
        long controlVersion,
        string reasonCode,
        DateTimeOffset now)
    {
        _context.AccountStatusHistory.Add(new AccountStatusHistoryRecord
        {
            InstitutionId = command.InstitutionId.Value,
            HistoryId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            Status = account.Status,
            ReasonCode = reasonCode,
            ActorId = command.ActorId.Value,
            OccurredAt = now,
        });
        AddStatusChangedFact(command.InstitutionId.Value, account, previousStatus, reasonCode, controlVersion, command.CorrelationId, now);
        AddAudit(command.InstitutionId, command.ActorId, "account.dormancy.assess", "account.dormancy.mark_dormant", account.AccountId, [reasonCode], command.CorrelationId, now);
    }

    private void RecordStatusChange(
        ReactivateAccountCommand command,
        AccountRecord account,
        string previousStatus,
        long controlVersion,
        string reasonCode,
        DateTimeOffset now)
    {
        _context.AccountStatusHistory.Add(new AccountStatusHistoryRecord
        {
            InstitutionId = command.InstitutionId.Value,
            HistoryId = Guid.NewGuid().ToString("N"),
            AccountId = account.AccountId,
            Status = account.Status,
            ReasonCode = reasonCode,
            ActorId = command.ActorId.Value,
            OccurredAt = now,
        });
        AddStatusChangedFact(command.InstitutionId.Value, account, previousStatus, reasonCode, controlVersion, command.CorrelationId, now);
        AddAudit(command.InstitutionId, command.ActorId, "account.dormancy.reactivate", "account.dormancy.reactivate", account.AccountId, [reasonCode], command.CorrelationId, now);
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

    private void AddAudit(
        InstitutionId institutionId,
        ActorId actorId,
        string permission,
        string action,
        string resourceId,
        IEnumerable<string> reasonCodes,
        string correlationId,
        DateTimeOffset now)
    {
        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = institutionId.Value,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = actorId.Value,
            ActorType = "human",
            Permission = permission,
            Action = action,
            ResourceType = "account.account",
            ResourceId = resourceId,
            Outcome = "succeeded",
            Reason = string.Join(',', reasonCodes),
            CorrelationId = correlationId,
            OccurredAt = now,
        });
    }

    private Task<long> CurrentControlVersionAsync(string accountId, CancellationToken cancellationToken) =>
        AccountControlVersionReader.ReadAsync(_context, accountId, cancellationToken);

    private async Task<AccountRecord> LoadAccountAsync(
        InstitutionId institutionId,
        string accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return await _context.Accounts
            .SingleOrDefaultAsync(record => record.AccountId == accountId.Trim(), cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_not_found", "The account is not available in this institution.");
    }

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

    private static DormancyAssessmentResult ToResult(
        AccountDormancyAssessmentRecord record,
        AccountRecord account,
        bool replayed) =>
        new(
            record.AccountId,
            record.AssessmentId,
            record.BusinessDate,
            record.Decision,
            record.TransitionPermitted,
            string.Equals(record.Decision, nameof(DormancyDecision.NoticeDue), StringComparison.Ordinal),
            record.ReasonCodes.Length == 0 ? [] : record.ReasonCodes.Split(','),
            record.LastQualifyingActivityDate,
            record.InactivityDays,
            record.ThresholdDays,
            record.RuleVersionId,
            account.Status,
            account.Version,
            replayed);

    private static string SerialiseAssessment(DormancyAssessmentResult result) =>
        System.Text.Json.JsonSerializer.Serialize(result);

    private static string SerialiseReactivation(
        AccountRecord account,
        string reasonCode,
        DateTimeOffset now,
        bool replayed) =>
        System.Text.Json.JsonSerializer.Serialize(new AccountReactivatedResult(
            account.AccountId,
            account.MaskedAccountNumber,
            account.Status,
            reasonCode,
            account.Version,
            now,
            replayed));

    private static DormancyAssessmentResult Decode(IdempotencyOutcome outcome, bool replayed)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (string.IsNullOrWhiteSpace(outcome.Result))
        {
            throw new InvalidOperationException("The dormancy assessment did not produce an idempotent result.");
        }

        DormancyAssessmentResult? decoded = System.Text.Json.JsonSerializer.Deserialize<DormancyAssessmentResult>(outcome.Result);
        return decoded is null
            ? throw new InvalidOperationException("The stored dormancy assessment is not readable.")
            : decoded with { Replayed = replayed };
    }

    private static AccountApplicationRejectedException Reject(string code, string message) => new(code, message);
}
