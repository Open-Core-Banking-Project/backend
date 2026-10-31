using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.IdentityAccess.Application.Devices;
using CoreBanking.IdentityAccess.Domain.Authentication;
using CoreBanking.IdentityAccess.Domain.Identities;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IdentityAccess.Infrastructure.Identity;

/// <summary>
/// Trusted devices: step-up-gated registration, effective-dated trust and transactional revocation that
/// terminates every session bound to the device. Only a public-key thumbprint is stored.
/// Decision: P1-E02-D11, epic invariants 11 and 12.
/// </summary>
public sealed class TrustedDeviceService : ITrustedDeviceService
{
    public const string Producer = "identity-access";
    public const string EventFamily = "identity";
    public const string RegisteredEventType = "trusted_device_registered";
    public const string RevokedEventType = "trusted_device_revoked";
    public const string SessionsRevokedEventType = "sessions_revoked_for_device";
    public const string SessionRevokedEventType = "session_revoked";

    private readonly IdentityAccessDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly TrustDurationPolicy _policy;

    public TrustedDeviceService(
        IdentityAccessDbContext context,
        TimeProvider timeProvider,
        TrustDurationPolicy policy)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _policy.Validate();
    }

    public async Task<TrustedDeviceView> StartRegistrationAsync(
        StartDeviceRegistrationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuthoriseSelf(command.Actor, command.IdentityId);

        string thumbprint = command.KeyThumbprint.Trim();
        TrustedDeviceRecord? existing = await _context.TrustedDevices
            .Where(device => device.IdentityId == command.IdentityId
                && device.KeyThumbprint == thumbprint
                && (device.TrustState == DeviceTrustState.Trusted || device.TrustState == DeviceTrustState.PendingStepUp))
            .OrderByDescending(device => device.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            // One registration per key: repeating the start returns the same pending device.
            return ToView(existing, _timeProvider.GetUtcNow());
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        TrustedDevice device;
        try
        {
            device = TrustedDevice.StartRegistration(
                command.Actor.InstitutionId.Value,
                command.IdentityId,
                thumbprint,
                command.Platform,
                command.DisplayName,
                now);
        }
        catch (ArgumentException exception)
        {
            throw new TrustedDeviceException("device_registration_invalid", exception.Message);
        }

        _context.TrustedDevices.Add(ToRecord(device));
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToView(device, now);
    }

    public async Task<TrustedDeviceView> CompleteRegistrationAsync(
        CompleteDeviceRegistrationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        TrustedDeviceRecord record = await LoadAsync(command.DeviceId, cancellationToken).ConfigureAwait(false);
        AuthoriseSelf(command.Actor, record.IdentityId);

        TrustedDevice device = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        StepUpEvidenceRecord? evidence = await ConsumeRegistrationStepUpAsync(command.Actor, now, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new TrustedDeviceException(
                "device_step_up_required",
                "Registering a trusted device requires a fresh step-up for the device-registration purpose.");

        TimeSpan duration = await DurationForAsync(record.IdentityId, cancellationToken).ConfigureAwait(false);
        try
        {
            device.Activate(evidence.EvidenceId, now.Add(duration), now);
        }
        catch (InvalidOperationException exception)
        {
            throw new TrustedDeviceException("device_state_invalid", exception.Message);
        }

        Apply(device, record);

        AddAuthenticationEvent(command.Actor, "trusted_device_registered", "device trust activated", now);
        AddOutbox(
            record.InstitutionId,
            RegisteredEventType,
            device.DeviceId,
            command.CorrelationId,
            now,
            new
            {
                deviceId = device.DeviceId,
                identityId = device.IdentityId,
                institutionId = record.InstitutionId,
                platform = DeviceCodes.ToCode(device.Platform),
            });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToView(device, now);
    }

    public async Task<IReadOnlyList<TrustedDeviceView>> ListAsync(
        ListDevicesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!query.OtherIdentityRequested)
        {
            AuthoriseSelf(query.Actor, query.IdentityId);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        List<TrustedDeviceRecord> records = await _context.TrustedDevices
            .AsNoTracking()
            .Where(device => device.IdentityId == query.IdentityId)
            .OrderByDescending(device => device.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return [.. records.Select(record => ToView(record, now))];
    }

    public async Task<TrustedDeviceView> SuspendAsync(SuspendDeviceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        TrustedDeviceRecord record = await LoadAsync(command.DeviceId, cancellationToken).ConfigureAwait(false);
        TrustedDevice device = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        try
        {
            device.Suspend(now);
        }
        catch (InvalidOperationException exception)
        {
            throw new TrustedDeviceException("device_state_invalid", exception.Message);
        }

        Apply(device, record);
        AddAuthenticationEvent(command.Actor, "trusted_device_suspended", "device suspended", now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToView(device, now);
    }

    public async Task<DeviceRevocationView> RevokeAsync(RevokeDeviceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        TrustedDeviceRecord record = await LoadAsync(command.DeviceId, cancellationToken).ConfigureAwait(false);
        if (!command.Administrative)
        {
            AuthoriseSelf(command.Actor, record.IdentityId);
        }

        TrustedDevice device = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (device.State == DeviceTrustState.Revoked)
        {
            return new DeviceRevocationView(device.DeviceId, DeviceCodes.ToCode(device.State), 0, Replayed: true);
        }

        try
        {
            device.Revoke(command.Reason, now);
        }
        catch (InvalidOperationException exception)
        {
            throw new TrustedDeviceException("device_state_invalid", exception.Message);
        }

        Apply(device, record);

        // Invariant 12: every active session bound to the device is revoked in this same transaction.
        List<SessionRecord> boundSessions = await _context.Sessions
            .Where(session => session.DeviceId == device.DeviceId
                && (session.Status == SessionLifecycleState.Active || session.Status == SessionLifecycleState.Refreshed))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (SessionRecord session in boundSessions)
        {
            string revocationEventId = Guid.NewGuid().ToString("N");
            session.Status = SessionLifecycleState.Revoked;
            session.RevokedAt = now;
            session.ProviderRevocationRequestedAt = now;
            session.RevocationEventId = revocationEventId;
            session.ConcurrencyVersion++;

            AddOutbox(
                record.InstitutionId,
                SessionRevokedEventType,
                session.SessionId,
                command.CorrelationId,
                now,
                new { sessionId = session.SessionId, status = "revoked" });
        }

        AddAuthenticationEvent(
            command.Actor,
            "trusted_device_revoked",
            boundSessions.Count == 0 ? "no_bound_sessions" : "bound_sessions_revoked",
            now);

        AddOutbox(
            record.InstitutionId,
            RevokedEventType,
            device.DeviceId,
            command.CorrelationId,
            now,
            new
            {
                deviceId = device.DeviceId,
                identityId = device.IdentityId,
                institutionId = record.InstitutionId,
                reasonCategory = "revoked",
            });

        AddOutbox(
            record.InstitutionId,
            SessionsRevokedEventType,
            device.DeviceId,
            command.CorrelationId,
            now,
            new
            {
                deviceId = device.DeviceId,
                identityId = device.IdentityId,
                institutionId = record.InstitutionId,
                revokedSessionCount = boundSessions.Count,
            });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new DeviceRevocationView(device.DeviceId, DeviceCodes.ToCode(device.State), boundSessions.Count, Replayed: false);
    }

    private async Task<StepUpEvidenceRecord?> ConsumeRegistrationStepUpAsync(
        AuthenticatedActor actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actor.SessionId))
        {
            return null;
        }

        StepUpEvidenceRecord? evidence = await _context.StepUpEvidence
            .Where(record => record.InstitutionId == actor.InstitutionId.Value
                && record.IdentityId == actor.Id.Value
                && record.SessionId == actor.SessionId
                && record.Purpose == TrustedDevice.RegistrationPurpose
                && record.ConsumedAt == null)
            .OrderByDescending(record => record.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (evidence is null
            || !StepUpEvidenceRules.IsValidFor(
                evidence.IssuedAt,
                evidence.ExpiresAt,
                evidence.ConsumedAt,
                evidence.Purpose,
                TrustedDevice.RegistrationPurpose,
                evidence.SessionId,
                actor.SessionId,
                evidence.DeviceId,
                actor.DeviceId,
                now))
        {
            return null;
        }

        evidence.ConsumedAt = now;
        return evidence;
    }

    private async Task<TimeSpan> DurationForAsync(string identityId, CancellationToken cancellationToken)
    {
        IdentityKind? kind = await _context.Identities
            .AsNoTracking()
            .Where(identity => identity.IdentityId == identityId)
            .Select(identity => (IdentityKind?)identity.IdentityType)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        // Privileged or administrative identities get the shorter trust window. Decision: P1-E02-D11.
        return kind is IdentityKind.Administrator or IdentityKind.Staff
            ? _policy.Privileged
            : _policy.Standard;
    }

    private async Task<TrustedDeviceRecord> LoadAsync(string deviceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        return await _context.TrustedDevices
            .SingleOrDefaultAsync(device => device.DeviceId == deviceId, cancellationToken).ConfigureAwait(false)
            ?? throw new TrustedDeviceException("device_not_found", "The device was not found.");
    }

    private static void AuthoriseSelf(AuthenticatedActor actor, string identityId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!string.Equals(actor.Id.Value, identityId?.Trim(), StringComparison.Ordinal))
        {
            throw new TrustedDeviceException(
                "device_scope_denied",
                "A device registration may only be managed for the authenticated identity.");
        }
    }

    private void AddAuthenticationEvent(AuthenticatedActor actor, string eventType, string detail, DateTimeOffset now) =>
        _context.AuthenticationEvents.Add(new AuthenticationEventRecord
        {
            InstitutionId = actor.InstitutionId.Value,
            AuthenticationEventId = Guid.NewGuid().ToString("N"),
            IdentityId = actor.Id.Value,
            EventType = eventType,
            Outcome = AuthenticationOutcome.Succeeded,
            Detail = detail,
            OccurredAt = now,
        });

    private void AddOutbox(
        string institutionId,
        string eventType,
        string businessReference,
        string correlationId,
        DateTimeOffset now,
        object payload) =>
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = institutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = Guid.NewGuid().ToString("N"),
            EventType = eventType,
            SchemaVersion = 1,
            Topic = EventContract.TopicFor(Producer, EventFamily, 1),
            BusinessReference = businessReference,
            CorrelationId = correlationId,
            Producer = Producer,
            Envelope = EventContract.Serialize(new
            {
                eventType,
                payload,
                occurredAt = now,
            }),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });

    private static TrustedDeviceRecord ToRecord(TrustedDevice device) => new()
    {
        InstitutionId = device.InstitutionId,
        DeviceId = device.DeviceId,
        IdentityId = device.IdentityId,
        KeyThumbprint = device.KeyThumbprint,
        Platform = DeviceCodes.ToCode(device.Platform),
        DisplayName = device.DisplayName,
        TrustState = device.State,
        CreatedAt = device.CreatedAt,
        TrustedAt = device.TrustedAt,
        TrustExpiresAt = device.TrustExpiresAt,
        RevokedAt = device.RevokedAt,
        RevocationReason = device.RevocationReason,
        StepUpEvidenceId = device.StepUpEvidenceId,
        ConcurrencyVersion = device.ConcurrencyVersion,
    };

    private static TrustedDevice ToModel(TrustedDeviceRecord record) =>
        TrustedDevice.Restore(
            record.InstitutionId,
            record.DeviceId,
            record.IdentityId,
            record.KeyThumbprint,
            DeviceCodes.FromPlatformCode(record.Platform),
            record.DisplayName,
            record.TrustState,
            record.CreatedAt,
            record.TrustedAt,
            record.TrustExpiresAt,
            record.RevokedAt,
            record.RevocationReason,
            record.StepUpEvidenceId,
            record.ConcurrencyVersion);

    private static void Apply(TrustedDevice device, TrustedDeviceRecord record)
    {
        record.TrustState = device.State;
        record.TrustedAt = device.TrustedAt;
        record.TrustExpiresAt = device.TrustExpiresAt;
        record.RevokedAt = device.RevokedAt;
        record.RevocationReason = device.RevocationReason;
        record.StepUpEvidenceId = device.StepUpEvidenceId;
        record.ConcurrencyVersion = device.ConcurrencyVersion;
    }

    private static TrustedDeviceView ToView(TrustedDeviceRecord record, DateTimeOffset now) =>
        ToView(ToModel(record), now);

    private static TrustedDeviceView ToView(TrustedDevice device, DateTimeOffset now) =>
        new(
            device.DeviceId,
            device.IdentityId,
            device.KeyThumbprint,
            DeviceCodes.ToCode(device.Platform),
            device.DisplayName,
            DeviceCodes.ToCode(device.State),
            device.CreatedAt,
            device.TrustedAt,
            device.TrustExpiresAt,
            device.RevokedAt,
            device.RevocationReason,
            device.IsTrustedAt(now),
            device.ConcurrencyVersion);
}
