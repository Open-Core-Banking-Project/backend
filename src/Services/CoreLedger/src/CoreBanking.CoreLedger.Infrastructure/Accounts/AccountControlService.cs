using System;
using System.Collections.Generic;
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
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Accounts;

/// <summary>
/// Owns the account controls: a typed restriction, a freeze or a block is stored with its declared scope and
/// half-open period, and the control-evaluation contract answers allow, deny or review from the approved
/// deny-dominant precedence. No control grants an ability and none writes a balance. Decision: P1-E04-D10.
/// </summary>
public sealed class AccountControlService : IAccountControlService
{
    private const string Producer = "core-ledger";
    private const string ControlEventFamily = "account";
    private const string RestrictionChangedEventType = "account_restriction_changed";
    private const int ControlSchemaVersion = 1;
    private const string ActiveStatus = "active";
    private const string ReleasedStatus = "released";
    private const string ClosedAccountStatus = "Closed";

    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public AccountControlService(
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

    public async Task<AccountControlChangeResult> ApplyRestrictionAsync(
        ApplyAccountRestrictionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.AccountId, command.CorrelationId);
        string direction = AccountControlDescriptor.ToToken(
            AccountControlDescriptor.ParseDirection(command.PostingDirectionCode));
        ValidatePeriod(command.EffectiveFrom, command.EffectiveTo);

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            command.RequestId, command.AccountId, command.RestrictionTypeCode, direction,
            string.Join(',', command.ChannelScopeCodes.OrderBy(code => code, StringComparer.Ordinal)),
            string.Join(',', command.TransactionTypeScopeCodes.OrderBy(code => code, StringComparer.Ordinal)),
            command.ReasonCode, command.EffectiveFrom.ToString("O"), command.EffectiveTo?.ToString("O") ?? string.Empty));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.restriction.apply", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => ApplyRestrictionCoreAsync(command, direction, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<AccountControlChangeResult>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountControlChangeResult> ApplyFreezeAsync(
        ApplyAccountFreezeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.AccountId, command.CorrelationId);
        if (command.FreezeTypeCode?.Trim().ToLowerInvariant() is not ("regulatory" or "institution"))
        {
            throw Reject("account_control_type_invalid", "A freeze must be regulatory or institution.");
        }

        if (string.IsNullOrWhiteSpace(command.AuthorityReference))
        {
            throw Reject("account_control_authority_required", "A freeze requires an authority reference.");
        }

        ValidatePeriod(command.EffectiveFrom, command.EffectiveTo);
        string typeCode = command.FreezeTypeCode.Trim().ToLowerInvariant();
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            command.RequestId, command.AccountId, typeCode, command.ReasonCode, command.AuthorityReference));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.freeze.apply", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => ApplyFreezeCoreAsync(command, typeCode, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<AccountControlChangeResult>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountControlChangeResult> ApplyBlockAsync(
        ApplyAccountBlockCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.AccountId, command.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.BlockTypeCode);
        ValidatePeriod(command.EffectiveFrom, command.EffectiveTo);

        string typeCode = command.BlockTypeCode.Trim().ToLowerInvariant();
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            command.RequestId, command.AccountId, typeCode,
            string.Join(',', command.OperationScopeCodes.OrderBy(code => code, StringComparer.Ordinal)),
            command.ReasonCode, command.EffectiveFrom.ToString("O")));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.block.apply", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => ApplyBlockCoreAsync(command, typeCode, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<AccountControlChangeResult>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountControlChangeResult> ReleaseAsync(
        ReleaseAccountControlCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.AccountId, command.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ControlId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ReasonCode);

        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            command.AccountId, command.ControlId, command.ControlKind, command.ReasonCode,
            command.ApprovalReference ?? string.Empty));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "account.control.release", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => ReleaseCoreAsync(command, token),
            cancellationToken).ConfigureAwait(false);
        return Decode<AccountControlChangeResult>(outcome, outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<AccountControlEvaluationResult> EvaluateAsync(
        EvaluateAccountControlsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.InstitutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.AccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.PostingIntentId);
        if (_tenantContext.CurrentInstitution != command.InstitutionId)
        {
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the account target.");
        }

        string accountId = command.AccountId.Trim();
        AccountRecord account = await _context.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.AccountId == accountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_not_found", "The account is not available in this institution.");
        if (string.Equals(account.Status, ClosedAccountStatus, StringComparison.Ordinal))
        {
            // A closed account rejects ordinary posting intents; only classified corrections are allowed elsewhere.
            return new AccountControlEvaluationResult(
                accountId,
                command.PostingIntentId.Trim(),
                AccountControlOutcomes.Deny,
                ["account_closed"],
                [],
                0,
                command.EvaluationTime);
        }

        IReadOnlyList<AccountControlDescriptor> controls = await LoadControlsAsync(accountId, cancellationToken)
            .ConfigureAwait(false);
        long currentVersion = controls.Count == 0
            ? 0
            : await CurrentControlVersionAsync(accountId, cancellationToken).ConfigureAwait(false);

        AccountControlDecision decision = AccountControlPrecedencePolicy.Evaluate(
            controls,
            new AccountControlIntent(
                command.PostingDirectionCode,
                command.TransactionTypeCode,
                command.ChannelCode,
                command.EvaluationTime),
            currentVersion,
            command.KnownControlVersion);

        return new AccountControlEvaluationResult(
            accountId,
            command.PostingIntentId.Trim(),
            decision.Outcome,
            decision.ReasonCodes,
            decision.EffectiveControls,
            currentVersion,
            command.EvaluationTime);
    }

    private async Task<string?> ApplyRestrictionCoreAsync(
        ApplyAccountRestrictionCommand command,
        string direction,
        CancellationToken cancellationToken)
    {
        await EnsureAccountControllableAsync(command.InstitutionId, command.AccountId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        long controlVersion = await NextControlVersionAsync(command.AccountId, cancellationToken).ConfigureAwait(false);
        string restrictionId = Guid.NewGuid().ToString("N");
        AccountRestrictionRecord record = new()
        {
            InstitutionId = command.InstitutionId.Value,
            RestrictionId = restrictionId,
            AccountId = command.AccountId.Trim(),
            RestrictionTypeCode = ValidateCode(command.RestrictionTypeCode, nameof(command.RestrictionTypeCode)),
            PostingDirectionCode = direction,
            ChannelScopeJson = JsonSerializer.Serialize(Normalise(command.ChannelScopeCodes)),
            TransactionTypeScopeJson = JsonSerializer.Serialize(Normalise(command.TransactionTypeScopeCodes)),
            ReasonCode = ValidateCode(command.ReasonCode, nameof(command.ReasonCode)),
            AuthorityReference = command.AuthorityReference?.Trim(),
            Status = ActiveStatus,
            RequiresManualRelease = false,
            RequestId = ValidateCode(command.RequestId, nameof(command.RequestId)),
            AppliedByActorId = command.ActorId.Value,
            ControlVersion = controlVersion,
            EffectiveFrom = command.EffectiveFrom,
            EffectiveTo = command.EffectiveTo,
        };
        _context.AccountRestrictions.Add(record);
        AddAudit(command.InstitutionId, command.ActorId, AccountPermissions.RestrictionApply.Value, "account.restriction.apply", restrictionId, command.ReasonCode, command.CorrelationId, now);
        AddRestrictionEvent(command, record, cancellationToken: cancellationToken);
        await SaveAsync("account_control_conflict", "The account controls changed while the restriction was being applied.", cancellationToken).ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Serialize(ToResult(record, "restriction", now, replayed: false));
    }

    private async Task<string?> ApplyFreezeCoreAsync(
        ApplyAccountFreezeCommand command,
        string typeCode,
        CancellationToken cancellationToken)
    {
        await EnsureAccountControllableAsync(command.InstitutionId, command.AccountId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        long controlVersion = await NextControlVersionAsync(command.AccountId, cancellationToken).ConfigureAwait(false);
        string freezeId = Guid.NewGuid().ToString("N");
        AccountFreezeRecord record = new()
        {
            InstitutionId = command.InstitutionId.Value,
            FreezeId = freezeId,
            AccountId = command.AccountId.Trim(),
            FreezeTypeCode = typeCode,
            ReasonCode = ValidateCode(command.ReasonCode, nameof(command.ReasonCode)),
            AuthorityReference = command.AuthorityReference.Trim(),
            Status = ActiveStatus,
            RequiresManualRelease = string.Equals(typeCode, "regulatory", StringComparison.Ordinal),
            RequestId = ValidateCode(command.RequestId, nameof(command.RequestId)),
            AppliedByActorId = command.ActorId.Value,
            ControlVersion = controlVersion,
            EffectiveFrom = command.EffectiveFrom,
            EffectiveTo = command.EffectiveTo,
        };
        _context.AccountFreezes.Add(record);
        AddAudit(command.InstitutionId, command.ActorId, AccountPermissions.RestrictionApply.Value, "account.freeze.apply", freezeId, command.ReasonCode, command.CorrelationId, now);
        await SaveAsync("account_control_conflict", "The account controls changed while the freeze was being applied.", cancellationToken).ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Serialize(ToResult(record, "freeze", now, replayed: false));
    }

    private async Task<string?> ApplyBlockCoreAsync(
        ApplyAccountBlockCommand command,
        string typeCode,
        CancellationToken cancellationToken)
    {
        await EnsureAccountControllableAsync(command.InstitutionId, command.AccountId, cancellationToken).ConfigureAwait(false);
        if (string.Equals(typeCode, "legal", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(command.AuthorityReference))
        {
            throw Reject("account_control_authority_required", "A legal block requires an authority reference.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        long controlVersion = await NextControlVersionAsync(command.AccountId, cancellationToken).ConfigureAwait(false);
        string blockId = Guid.NewGuid().ToString("N");
        AccountBlockRecord record = new()
        {
            InstitutionId = command.InstitutionId.Value,
            BlockId = blockId,
            AccountId = command.AccountId.Trim(),
            BlockTypeCode = typeCode,
            OperationScopeJson = JsonSerializer.Serialize(Normalise(command.OperationScopeCodes)),
            ReasonCode = ValidateCode(command.ReasonCode, nameof(command.ReasonCode)),
            AuthorityReference = command.AuthorityReference?.Trim(),
            Status = ActiveStatus,
            RequiresManualRelease = string.Equals(typeCode, "legal", StringComparison.Ordinal),
            RequestId = ValidateCode(command.RequestId, nameof(command.RequestId)),
            AppliedByActorId = command.ActorId.Value,
            ControlVersion = controlVersion,
            EffectiveFrom = command.EffectiveFrom,
            EffectiveTo = command.EffectiveTo,
        };
        _context.AccountBlocks.Add(record);
        AddAudit(command.InstitutionId, command.ActorId, AccountPermissions.RestrictionApply.Value, "account.block.apply", blockId, command.ReasonCode, command.CorrelationId, now);
        await SaveAsync("account_control_conflict", "The account controls changed while the block was being applied.", cancellationToken).ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Serialize(ToResult(record, "block", now, replayed: false));
    }

    private async Task<string?> ReleaseCoreAsync(
        ReleaseAccountControlCommand command,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string controlId = command.ControlId.Trim();
        string kind = command.ControlKind.Trim().ToLowerInvariant();
        AccountControlChangeResult result = kind switch
        {
            "restriction" => await ReleaseRestrictionAsync(command, controlId, now, cancellationToken).ConfigureAwait(false),
            "freeze" => await ReleaseFreezeAsync(command, controlId, now, cancellationToken).ConfigureAwait(false),
            "block" => await ReleaseBlockAsync(command, controlId, now, cancellationToken).ConfigureAwait(false),
            _ => throw Reject("account_control_kind_invalid", "The control kind must be restriction, freeze or block."),
        };
        await SaveAsync("account_control_conflict", "The account controls changed while the control was being released.", cancellationToken).ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Serialize(result);
    }

    private async Task<AccountControlChangeResult> ReleaseRestrictionAsync(
        ReleaseAccountControlCommand command,
        string controlId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        AccountRestrictionRecord record = await _context.AccountRestrictions
            .SingleOrDefaultAsync(
                candidate => candidate.RestrictionId == controlId && candidate.AccountId == command.AccountId.Trim(),
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_control_not_found", "The restriction is not available for this account.");
        if (string.Equals(record.Status, ReleasedStatus, StringComparison.Ordinal))
        {
            return ToResult(record, "restriction", now, replayed: true);
        }

        record.Status = ReleasedStatus;
        record.ReleasedByActorId = command.ActorId.Value;
        record.ReleasedAt = now;
        record.ControlVersion = await NextControlVersionAsync(command.AccountId, cancellationToken).ConfigureAwait(false);
        AddAudit(command.InstitutionId, command.ActorId, AccountPermissions.RestrictionRelease.Value, "account.restriction.release", controlId, command.ReasonCode, command.CorrelationId, now);
        return ToResult(record, "restriction", now, replayed: false);
    }

    private async Task<AccountControlChangeResult> ReleaseFreezeAsync(
        ReleaseAccountControlCommand command,
        string controlId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        AccountFreezeRecord record = await _context.AccountFreezes
            .SingleOrDefaultAsync(
                candidate => candidate.FreezeId == controlId && candidate.AccountId == command.AccountId.Trim(),
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_control_not_found", "The freeze is not available for this account.");
        if (string.Equals(record.Status, ReleasedStatus, StringComparison.Ordinal))
        {
            return new AccountControlChangeResult(record.FreezeId, record.AccountId, "freeze", record.FreezeTypeCode, record.Status, record.ControlVersion, record.EffectiveFrom, record.EffectiveTo, now, true);
        }

        RequireDualControl(record.RequiresManualRelease, command);
        record.Status = ReleasedStatus;
        record.ReleasedByActorId = command.ActorId.Value;
        record.ReleasedAt = now;
        record.ControlVersion = await NextControlVersionAsync(command.AccountId, cancellationToken).ConfigureAwait(false);
        AddAudit(command.InstitutionId, command.ActorId, AccountPermissions.RestrictionRelease.Value, "account.freeze.release", controlId, command.ReasonCode, command.CorrelationId, now);
        return new AccountControlChangeResult(record.FreezeId, record.AccountId, "freeze", record.FreezeTypeCode, record.Status, record.ControlVersion, record.EffectiveFrom, record.EffectiveTo, now, false);
    }

    private async Task<AccountControlChangeResult> ReleaseBlockAsync(
        ReleaseAccountControlCommand command,
        string controlId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        AccountBlockRecord record = await _context.AccountBlocks
            .SingleOrDefaultAsync(
                candidate => candidate.BlockId == controlId && candidate.AccountId == command.AccountId.Trim(),
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_control_not_found", "The block is not available for this account.");
        if (string.Equals(record.Status, ReleasedStatus, StringComparison.Ordinal))
        {
            return new AccountControlChangeResult(record.BlockId, record.AccountId, "block", record.BlockTypeCode, record.Status, record.ControlVersion, record.EffectiveFrom, record.EffectiveTo, now, true);
        }

        RequireDualControl(record.RequiresManualRelease, command);
        record.Status = ReleasedStatus;
        record.ReleasedByActorId = command.ActorId.Value;
        record.ReleasedAt = now;
        record.ControlVersion = await NextControlVersionAsync(command.AccountId, cancellationToken).ConfigureAwait(false);
        AddAudit(command.InstitutionId, command.ActorId, AccountPermissions.RestrictionRelease.Value, "account.block.release", controlId, command.ReasonCode, command.CorrelationId, now);
        return new AccountControlChangeResult(record.BlockId, record.AccountId, "block", record.BlockTypeCode, record.Status, record.ControlVersion, record.EffectiveFrom, record.EffectiveTo, now, false);
    }

    /// <summary>
    /// A manually releasable control needs a release approval reference and a releaser who did not apply it.
    /// Decision: P1-E04-D10.
    /// </summary>
    private static void RequireDualControl(bool requiresManualRelease, ReleaseAccountControlCommand command)
    {
        if (!requiresManualRelease)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(command.ApprovalReference))
        {
            throw Reject("account_control_release_requires_approval", "Releasing this control requires an approval reference.");
        }
    }

    private async Task<IReadOnlyList<AccountControlDescriptor>> LoadControlsAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        var restrictions = await _context.AccountRestrictions
            .AsNoTracking()
            .Where(record => record.AccountId == accountId && record.Status == ActiveStatus)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var freezes = await _context.AccountFreezes
            .AsNoTracking()
            .Where(record => record.AccountId == accountId && record.Status == ActiveStatus)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var blocks = await _context.AccountBlocks
            .AsNoTracking()
            .Where(record => record.AccountId == accountId && record.Status == ActiveStatus)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        List<AccountControlDescriptor> controls = [];
        controls.AddRange(restrictions.Select(record => new AccountControlDescriptor(
            record.RestrictionId,
            AccountControlType.TypedRestriction,
            record.Status,
            AccountControlDescriptor.ParseDirection(record.PostingDirectionCode),
            JsonSerializer.Deserialize<string[]>(record.ChannelScopeJson) ?? [],
            JsonSerializer.Deserialize<string[]>(record.TransactionTypeScopeJson) ?? [],
            record.EffectiveFrom,
            record.EffectiveTo,
            record.RequiresManualRelease)));
        controls.AddRange(freezes.Select(record => new AccountControlDescriptor(
            record.FreezeId,
            string.Equals(record.FreezeTypeCode, "regulatory", StringComparison.Ordinal)
                ? AccountControlType.RegulatoryFreeze
                : AccountControlType.InstitutionFreeze,
            record.Status,
            AccountControlDirection.Both,
            [],
            [],
            record.EffectiveFrom,
            record.EffectiveTo,
            record.RequiresManualRelease)));
        controls.AddRange(blocks.Select(record => new AccountControlDescriptor(
            record.BlockId,
            string.Equals(record.BlockTypeCode, "legal", StringComparison.Ordinal)
                ? AccountControlType.LegalBlock
                : AccountControlType.ProductPolicyRestriction,
            record.Status,
            AccountControlDirection.Both,
            [],
            JsonSerializer.Deserialize<string[]>(record.OperationScopeJson) ?? [],
            record.EffectiveFrom,
            record.EffectiveTo,
            record.RequiresManualRelease)));
        return controls;
    }

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

    private async Task<long> NextControlVersionAsync(string accountId, CancellationToken cancellationToken) =>
        await CurrentControlVersionAsync(accountId.Trim(), cancellationToken).ConfigureAwait(false) + 1;

    private async Task EnsureAccountControllableAsync(
        InstitutionId institutionId,
        string accountId,
        CancellationToken cancellationToken)
    {
        AccountRecord account = await _context.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.AccountId == accountId.Trim(), cancellationToken)
            .ConfigureAwait(false)
            ?? throw Reject("account_not_found", "The account is not available in this institution.");
        if (string.Equals(account.Status, ClosedAccountStatus, StringComparison.Ordinal))
        {
            throw Reject("account_closed", "A closed account cannot receive a new control.");
        }
    }

    private void AddRestrictionEvent(
        ApplyAccountRestrictionCommand command,
        AccountRestrictionRecord record,
        CancellationToken cancellationToken)
    {
        string eventId = Guid.NewGuid().ToString("N");
        AccountRestrictionChangedV1 payload = new(
            record.InstitutionId,
            record.AccountId,
            record.RestrictionId,
            record.RestrictionTypeCode,
            record.PostingDirectionCode,
            JsonSerializer.Deserialize<string[]>(record.ChannelScopeJson) ?? [],
            record.Status,
            record.EffectiveFrom,
            record.EffectiveTo,
            record.ControlVersion,
            _timeProvider.GetUtcNow());
        AddOutbox(record.InstitutionId, eventId, RestrictionChangedEventType, command.CorrelationId, EventContract.Serialize(payload), record.RestrictionId);
    }

    private void AddOutbox(
        string institutionId,
        string eventId,
        string eventType,
        string correlationId,
        string payload,
        string businessReference)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = eventType,
            SchemaVersion = ControlSchemaVersion,
            OccurredAt = now,
            InstitutionId = institutionId,
            CorrelationId = correlationId,
            Producer = Producer,
            BusinessReference = businessReference,
            Payload = payload,
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = institutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = eventType,
            SchemaVersion = ControlSchemaVersion,
            Topic = EventContract.TopicFor(Producer, ControlEventFamily, ControlSchemaVersion),
            BusinessReference = businessReference,
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
        string reason,
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
            ResourceType = "account.control",
            ResourceId = resourceId,
            Outcome = "succeeded",
            Reason = reason,
            CorrelationId = correlationId,
            OccurredAt = now,
        });
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

    private static void ValidatePeriod(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo)
    {
        if (effectiveTo is not null && effectiveTo <= effectiveFrom)
        {
            throw Reject("account_control_period_invalid", "The effective period must be half-open and non-empty.");
        }
    }

    private static string ValidateCode(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string trimmed = value.Trim();
        if (trimmed.Length > 64 || trimmed.Contains('|'))
        {
            throw Reject("account_control_code_invalid", "A control code may contain at most 64 characters and no separator.");
        }

        return trimmed;
    }

    private static string[] Normalise(IReadOnlyCollection<string>? values) =>
        values is null
            ? []
            : values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

    private static AccountControlChangeResult ToResult(
        AccountRestrictionRecord record,
        string kind,
        DateTimeOffset changedAt,
        bool replayed) =>
        new(record.RestrictionId, record.AccountId, kind, record.RestrictionTypeCode, record.Status,
            record.ControlVersion, record.EffectiveFrom, record.EffectiveTo, changedAt, replayed);

    private static AccountControlChangeResult ToResult(
        AccountFreezeRecord record,
        string kind,
        DateTimeOffset changedAt,
        bool replayed) =>
        new(record.FreezeId, record.AccountId, kind, record.FreezeTypeCode, record.Status,
            record.ControlVersion, record.EffectiveFrom, record.EffectiveTo, changedAt, replayed);

    private static AccountControlChangeResult ToResult(
        AccountBlockRecord record,
        string kind,
        DateTimeOffset changedAt,
        bool replayed) =>
        new(record.BlockId, record.AccountId, kind, record.BlockTypeCode, record.Status,
            record.ControlVersion, record.EffectiveFrom, record.EffectiveTo, changedAt, replayed);

    private static T Decode<T>(IdempotencyOutcome outcome, bool replayed) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (string.IsNullOrWhiteSpace(outcome.Result))
        {
            throw new InvalidOperationException("The control command did not produce an idempotent result.");
        }

        T? decoded = System.Text.Json.JsonSerializer.Deserialize<T>(outcome.Result);
        if (decoded is null)
        {
            throw new InvalidOperationException("The stored control evidence is not readable.");
        }

        return decoded switch
        {
            AccountControlChangeResult change => (T)(object)(change with { Replayed = replayed }),
            _ => decoded,
        };
    }

    private static AccountApplicationRejectedException Reject(string code, string message) => new(code, message);

    private static AccountApplicationRejectedException Reject(string code, string message, Exception inner) =>
        new(code, message, inner);
}
