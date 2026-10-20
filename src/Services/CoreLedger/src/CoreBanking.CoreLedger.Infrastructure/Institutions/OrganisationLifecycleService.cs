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
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Institutions;

public sealed class OrganisationLifecycleService : IOrganisationLifecycleService
{
    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public OrganisationLifecycleService(CoreLedgerDbContext context, IdempotencyService idempotency, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _context = context;
        _idempotency = idempotency;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public Task<OrganisationLifecycleResult> CreateRegionAsync(CreateRegionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RegionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Name);
        return ExecuteIdempotentAsync(
            command.InstitutionId, command.ActorId, command.IdempotencyKey, "institution.region.create",
            $"{command.RegionId}|{command.Code.Trim().ToLowerInvariant()}|{command.Name.Trim()}",
            token => CreateRegionCoreAsync(command, token), cancellationToken);
    }

    public Task<OrganisationLifecycleResult> CreateBranchAsync(CreateBranchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.BranchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RegionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Name);
        return ExecuteIdempotentAsync(
            command.InstitutionId, command.ActorId, command.IdempotencyKey, "institution.branch.create",
            $"{command.BranchId}|{command.RegionId}|{command.Code.Trim().ToLowerInvariant()}|{command.Name.Trim()}",
            token => CreateBranchCoreAsync(command, token), cancellationToken);
    }

    public Task<OrganisationLifecycleResult> ChangeRegionStatusAsync(ChangeOrganisationStatusCommand command, CancellationToken cancellationToken)
    {
        ValidateStatusCommand(command);
        return ExecuteIdempotentAsync(
            command.InstitutionId, command.ActorId, command.IdempotencyKey, $"institution.region.{command.TargetStatus}",
            $"{command.ResourceId}|{command.TargetStatus}|{command.Reason?.Trim()}",
            token => ChangeRegionStatusCoreAsync(command, token), cancellationToken);
    }

    public Task<OrganisationLifecycleResult> ChangeBranchStatusAsync(ChangeOrganisationStatusCommand command, CancellationToken cancellationToken)
    {
        ValidateStatusCommand(command);
        return ExecuteIdempotentAsync(
            command.InstitutionId, command.ActorId, command.IdempotencyKey, $"institution.branch.{command.TargetStatus}",
            $"{command.ResourceId}|{command.TargetStatus}|{command.Reason?.Trim()}",
            token => ChangeBranchStatusCoreAsync(command, token), cancellationToken);
    }

    private async Task<string?> CreateRegionCoreAsync(CreateRegionCommand command, CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        RegionRecord region = new()
        {
            InstitutionId = command.InstitutionId.Value,
            RegionId = command.RegionId.Trim(),
            Code = command.Code.Trim().ToLowerInvariant(),
            Name = command.Name.Trim(),
            Status = "draft",
            CreatedByActorId = command.ActorId.Value,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _context.Regions.Add(region);
        AddHistory(command.InstitutionId.Value, "region", region.RegionId, null, "draft", command.ActorId.Value, null, command.CorrelationId, now);
        string eventId = AddOutbox(command.InstitutionId.Value, "region_created", region.RegionId, command.CorrelationId,
            new RegionCreatedV1(command.InstitutionId.Value, region.RegionId, region.Code, region.Name, now), now);
        await SaveAsync("region_code_conflict", "The region ID or code is already in use.", cancellationToken).ConfigureAwait(false);
        return Encode(command.InstitutionId.Value, region.RegionId, eventId, "draft", now);
    }

    private async Task<string?> CreateBranchCoreAsync(CreateBranchCommand command, CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(cancellationToken).ConfigureAwait(false);
        RegionRecord region = await _context.Regions.SingleOrDefaultAsync(x => x.RegionId == command.RegionId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrganisationLifecycleRejectedException("region_not_found", "The region is not available in this institution.");
        if (!string.Equals(region.Status, "active", StringComparison.Ordinal))
        {
            throw new OrganisationLifecycleRejectedException("region_inactive", "A branch can only be created in an active region.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        BranchRecord branch = new()
        {
            InstitutionId = command.InstitutionId.Value,
            BranchId = command.BranchId.Trim(),
            RegionId = region.RegionId,
            Code = command.Code.Trim().ToLowerInvariant(),
            Name = command.Name.Trim(),
            Status = "draft",
            CreatedByActorId = command.ActorId.Value,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _context.Branches.Add(branch);
        AddHistory(command.InstitutionId.Value, "branch", branch.BranchId, null, "draft", command.ActorId.Value, null, command.CorrelationId, now);
        string eventId = AddOutbox(command.InstitutionId.Value, "branch_created", branch.BranchId, command.CorrelationId,
            new BranchCreatedV1(command.InstitutionId.Value, branch.BranchId, branch.RegionId, branch.Code, branch.Name, now), now);
        await SaveAsync("branch_code_conflict", "The branch ID or code is already in use.", cancellationToken).ConfigureAwait(false);
        return Encode(command.InstitutionId.Value, branch.BranchId, eventId, "draft", now);
    }

    private async Task<string?> ChangeRegionStatusCoreAsync(ChangeOrganisationStatusCommand command, CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(cancellationToken).ConfigureAwait(false);
        RegionRecord region = await _context.Regions.SingleOrDefaultAsync(x => x.RegionId == command.ResourceId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrganisationLifecycleRejectedException("region_not_found", "The region is not available in this institution.");
        EnsureIndependentMaker(region.CreatedByActorId, command.ActorId.Value, command.TargetStatus);
        string previous = region.Status;
        EnsureTransition("region", previous, command.TargetStatus, command.Reason);
        if ((command.TargetStatus is "suspended" or "closed")
            && await _context.Branches.AnyAsync(x => x.RegionId == region.RegionId && x.Status == "active", cancellationToken).ConfigureAwait(false))
        {
            throw new OrganisationLifecycleRejectedException("region_has_active_branches", "An active branch must be suspended or closed before its region can be suspended or closed.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        region.Status = command.TargetStatus;
        region.UpdatedAt = now;
        region.Version++;
        AddHistory(command.InstitutionId.Value, "region", region.RegionId, previous, region.Status, command.ActorId.Value, command.Reason?.Trim(), command.CorrelationId, now);
        string eventId = AddOutbox(command.InstitutionId.Value, "region_status_changed", region.RegionId, command.CorrelationId,
            new RegionStatusChangedV1(command.InstitutionId.Value, region.RegionId, previous, region.Status, command.Reason?.Trim(), now), now);
        await SaveAsync("region_transition_conflict", "The region lifecycle changed while the request was processed.", cancellationToken).ConfigureAwait(false);
        return Encode(command.InstitutionId.Value, region.RegionId, eventId, region.Status, now);
    }

    private async Task<string?> ChangeBranchStatusCoreAsync(ChangeOrganisationStatusCommand command, CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(cancellationToken).ConfigureAwait(false);
        BranchRecord branch = await _context.Branches.SingleOrDefaultAsync(x => x.BranchId == command.ResourceId, cancellationToken).ConfigureAwait(false)
            ?? throw new OrganisationLifecycleRejectedException("branch_not_found", "The branch is not available in this institution.");
        EnsureIndependentMaker(branch.CreatedByActorId, command.ActorId.Value, command.TargetStatus);
        string previous = branch.Status;
        EnsureTransition("branch", previous, command.TargetStatus, command.Reason);
        if (command.TargetStatus == "active")
        {
            bool regionActive = await _context.Regions.AnyAsync(x => x.RegionId == branch.RegionId && x.Status == "active", cancellationToken).ConfigureAwait(false);
            if (!regionActive)
            {
                throw new OrganisationLifecycleRejectedException("region_inactive", "The branch cannot be activated unless its region is active.");
            }
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        branch.Status = command.TargetStatus;
        branch.UpdatedAt = now;
        branch.Version++;
        AddHistory(command.InstitutionId.Value, "branch", branch.BranchId, previous, branch.Status, command.ActorId.Value, command.Reason?.Trim(), command.CorrelationId, now);
        string eventId = AddOutbox(command.InstitutionId.Value, "branch_status_changed", branch.BranchId, command.CorrelationId,
            new BranchStatusChangedV1(command.InstitutionId.Value, branch.BranchId, branch.RegionId, previous, branch.Status, command.Reason?.Trim(), now), now);
        await SaveAsync("branch_transition_conflict", "The branch lifecycle changed while the request was processed.", cancellationToken).ConfigureAwait(false);
        return Encode(command.InstitutionId.Value, branch.BranchId, eventId, branch.Status, now);
    }

    private async Task EnsureInstitutionActiveAsync(CancellationToken cancellationToken)
    {
        InstitutionRecord? institution = await _context.Institutions.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (institution is null || !string.Equals(institution.Status, "active", StringComparison.Ordinal))
        {
            throw new OrganisationLifecycleRejectedException("institution_inactive", "Region and branch lifecycle changes require an active institution.");
        }
    }

    private async Task<OrganisationLifecycleResult> ExecuteIdempotentAsync(InstitutionId institutionId, CoreBanking.BuildingBlocks.Domain.Identity.ActorId actorId, string keyValue, string operation, string fingerprintValue, Func<CancellationToken, Task<string?>> action, CancellationToken cancellationToken)
    {
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(institutionId.Value, operation, actorId.Value, keyValue),
            IdempotencyKey.Fingerprint(fingerprintValue), action, cancellationToken).ConfigureAwait(false);
        return outcome.Kind switch
        {
            IdempotencyOutcomeKind.Executed => Decode(outcome.Result, false),
            IdempotencyOutcomeKind.Replayed => Decode(outcome.Result, true),
            IdempotencyOutcomeKind.Conflict => throw new OrganisationLifecycleRejectedException("idempotency_key_reused_with_different_request", "The idempotency key was already used with a different request."),
            _ => throw new OrganisationLifecycleInProgressException("An equivalent lifecycle request is still in progress."),
        };
    }

    private void ValidateTarget(CoreBanking.BuildingBlocks.Domain.Identity.InstitutionId institutionId, string idempotencyKey, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (_tenantContext.CurrentInstitution != institutionId)
        {
            throw new OrganisationLifecycleRejectedException("cross_tenant_access_denied", "The authenticated institution does not match the lifecycle target.");
        }
    }

    private void ValidateStatusCommand(ChangeOrganisationStatusCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ResourceId);
        if (command.TargetStatus is not ("active" or "suspended" or "closed"))
        {
            throw new ArgumentException("Target status must be active, suspended or closed.", nameof(command));
        }
    }

    private static void EnsureIndependentMaker(string maker, string actor, string target)
    {
        if ((target is "active" or "suspended" or "closed") && string.Equals(maker, actor, StringComparison.Ordinal))
        {
            throw new OrganisationLifecycleRejectedException("maker_cannot_approve_own_change", "The actor that created the resource cannot approve its restricted lifecycle transition.");
        }
    }

    private static void EnsureTransition(string resource, string previous, string target, string? reason)
    {
        bool allowed = resource == "branch"
            ? (previous, target) is ("draft", "active") or ("active", "suspended") or ("suspended", "active") or ("suspended", "closed")
            : (previous, target) is ("draft", "active") or ("active", "suspended") or ("suspended", "active") or ("draft", "closed") or ("suspended", "closed");
        if (!allowed)
        {
            throw new OrganisationLifecycleRejectedException($"{resource}_transition_invalid", $"The {resource} cannot transition from {previous} to {target}.");
        }

        if (target is "suspended" or "closed" && string.IsNullOrWhiteSpace(reason))
        {
            throw new OrganisationLifecycleRejectedException("lifecycle_reason_required", "A reason is required for suspension or closure.");
        }
    }

    private void AddHistory(string institutionId, string type, string resourceId, string? previous, string next, string actor, string? reason, string correlationId, DateTimeOffset at) =>
        _context.OrganisationStatusHistory.Add(new OrganisationStatusHistoryRecord
        {
            InstitutionId = institutionId,
            HistoryId = Guid.NewGuid().ToString("N"),
            ResourceType = type,
            ResourceId = resourceId,
            PreviousStatus = previous,
            NewStatus = next,
            ChangedByActorId = actor,
            Reason = reason,
            CorrelationId = correlationId,
            ChangedAt = at,
        });

    private string AddOutbox<T>(string institutionId, string eventType, string reference, string correlationId, T payload, DateTimeOffset at)
    {
        string eventId = Guid.NewGuid().ToString("N");
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = eventType,
            SchemaVersion = 1,
            OccurredAt = at,
            InstitutionId = institutionId,
            CorrelationId = correlationId,
            Producer = InstitutionCreationService.Producer,
            BusinessReference = reference,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = institutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = eventType,
            SchemaVersion = 1,
            Topic = EventContract.TopicFor(InstitutionCreationService.Producer, InstitutionCreationService.EventFamily, 1),
            BusinessReference = reference,
            CorrelationId = correlationId,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            Producer = InstitutionCreationService.Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = at,
            CreatedAt = at,
        });
        return eventId;
    }

    private async Task SaveAsync(string conflictCode, string conflictMessage, CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            DetachFailedLifecycleChanges();
            throw new OrganisationLifecycleRejectedException(conflictCode, conflictMessage, exception);
        }
        catch (DbUpdateException exception)
        {
            DetachFailedLifecycleChanges();
            throw new OrganisationLifecycleRejectedException(conflictCode, conflictMessage, exception);
        }
    }

    private void DetachFailedLifecycleChanges()
    {
        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry in _context.ChangeTracker.Entries()
                     .Where(entry => entry.Entity is not IdempotencyRecord && entry.State != EntityState.Unchanged))
        {
            entry.State = EntityState.Detached;
        }
    }

    private static string Encode(string institutionId, string resourceId, string eventId, string status, DateTimeOffset at) =>
        string.Join('|', institutionId, resourceId, eventId, status, at.ToString("O", CultureInfo.InvariantCulture));

    private static OrganisationLifecycleResult Decode(string? value, bool replayed)
    {
        string[] parts = (value ?? string.Empty).Split('|');
        if (parts.Length != 5 || !DateTimeOffset.TryParseExact(parts[4], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset at))
        {
            throw new InvalidOperationException("The recorded idempotency outcome is not an organisation lifecycle outcome.");
        }

        return new OrganisationLifecycleResult(parts[0], parts[1], parts[2], parts[3], at, replayed);
    }
}
