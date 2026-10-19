using System;
using System.Collections.Generic;
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
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Institutions;

public sealed class BusinessCalendarService : IBusinessCalendarService
{
    private const string Producer = "core-ledger";
    private const string EventFamily = "institution-lifecycle";
    private const int SchemaVersion = 1;

    private readonly CoreLedgerDbContext _context;
    private readonly IdempotencyService _idempotency;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public BusinessCalendarService(CoreLedgerDbContext context, IdempotencyService idempotency, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CalendarCommandResult> CreateVersionAsync(CreateCalendarVersionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        BusinessCalendarVersion version = BusinessCalendarVersion.Create(
            command.InstitutionId, command.CalendarId, command.VersionId, command.VersionNumber, command.TimeZoneId,
            command.EffectiveFrom, command.EffectiveTo, command.InitialBusinessDate, command.ActorId, command.Days);
        string fingerprint = IdempotencyKey.Fingerprint(string.Join('|',
            version.CalendarId, version.VersionId, version.VersionNumber, version.TimeZoneId,
            version.EffectiveFrom.ToString("O", CultureInfo.InvariantCulture),
            version.EffectiveTo.ToString("O", CultureInfo.InvariantCulture),
            version.InitialBusinessDate?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            string.Join(';', version.Days.OrderBy(x => x.Date).Select(x => $"{x.Date:O}:{x.Classification}:{x.IsProcessingDay}"))));
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "institution.calendar.version.create", command.ActorId.Value, command.IdempotencyKey),
            fingerprint,
            token => CreateVersionCoreAsync(version, token),
            cancellationToken).ConfigureAwait(false);
        return DecodeCalendarOutcome(outcome, replayed: outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<CalendarCommandResult> PublishVersionAsync(PublishCalendarVersionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.VersionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ApprovalReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Reason);
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "institution.calendar.version.publish", command.ActorId.Value, command.IdempotencyKey),
            IdempotencyKey.Fingerprint($"{command.VersionId}|{command.ApprovalReference}|{command.Reason.Trim()}"),
            token => PublishVersionCoreAsync(command, token),
            cancellationToken).ConfigureAwait(false);
        return DecodeCalendarOutcome(outcome, replayed: outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<BusinessDateAdvanceResult> RequestAdvanceAsync(RequestBusinessDateAdvanceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CalendarVersionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ProcessingCheckpointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Reason);
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "institution.business_date.advance.request", command.ActorId.Value, command.IdempotencyKey),
            IdempotencyKey.Fingerprint($"{command.RequestId}|{command.ExpectedCurrentDate:O}|{command.NewBusinessDate:O}|{command.CalendarVersionId}|{command.ProcessingCheckpointId}|{command.Reason.Trim()}"),
            token => RequestAdvanceCoreAsync(command, token),
            cancellationToken).ConfigureAwait(false);
        return DecodeBusinessDateOutcome(outcome, replayed: outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    public async Task<BusinessDateAdvanceResult> ApproveAdvanceAsync(ApproveBusinessDateAdvanceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateTarget(command.InstitutionId, command.IdempotencyKey, command.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ApprovalReference);
        IdempotencyOutcome outcome = await _idempotency.ExecuteAsync(
            IdempotencyKey.Create(command.InstitutionId.Value, "institution.business_date.advance.approve", command.ActorId.Value, command.IdempotencyKey),
            IdempotencyKey.Fingerprint($"{command.RequestId}|{command.ApprovalReference}"),
            token => ApproveAdvanceCoreAsync(command, token),
            cancellationToken).ConfigureAwait(false);
        return DecodeBusinessDateOutcome(outcome, replayed: outcome.Kind == IdempotencyOutcomeKind.Replayed);
    }

    private async Task<string?> CreateVersionCoreAsync(BusinessCalendarVersion version, CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        BusinessCalendarRecord? calendar = await _context.BusinessCalendars
            .SingleOrDefaultAsync(x => x.CalendarId == version.CalendarId, cancellationToken).ConfigureAwait(false);
        if (calendar is null)
        {
            _context.BusinessCalendars.Add(new BusinessCalendarRecord
            {
                InstitutionId = version.InstitutionId.Value,
                CalendarId = version.CalendarId,
                Name = version.CalendarId,
                Version = 1,
                CreatedAt = now,
            });
        }

        _context.CalendarVersions.Add(new CalendarVersionRecord
        {
            InstitutionId = version.InstitutionId.Value,
            VersionId = version.VersionId,
            CalendarId = version.CalendarId,
            VersionNumber = version.VersionNumber,
            TimeZoneId = version.TimeZoneId,
            EffectiveFrom = version.EffectiveFrom,
            EffectiveTo = version.EffectiveTo,
            InitialBusinessDate = version.InitialBusinessDate,
            Status = "draft",
            CreatedByActorId = version.CreatedBy.Value,
            Version = 1,
            CreatedAt = now,
        });
        _context.CalendarDays.AddRange(version.Days.Select(day => new CalendarDayRecord
        {
            InstitutionId = version.InstitutionId.Value,
            VersionId = version.VersionId,
            Date = day.Date,
            Classification = ToStorage(day.Classification),
            IsProcessingDay = day.IsProcessingDay,
        }));

        await SaveAsync("calendar_version_conflict", "The calendar version identifier or version number is already in use.", cancellationToken).ConfigureAwait(false);
        return EncodeCalendar(version.InstitutionId.Value, version.VersionId, null, "draft", now);
    }

    private async Task<string?> PublishVersionCoreAsync(PublishCalendarVersionCommand command, CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(cancellationToken).ConfigureAwait(false);
        CalendarVersionRecord record = await _context.CalendarVersions
            .SingleOrDefaultAsync(x => x.VersionId == command.VersionId, cancellationToken).ConfigureAwait(false)
            ?? throw Reject("calendar_version_not_found", "The calendar version is not available in this institution.");
        if (!string.Equals(record.Status, "draft", StringComparison.Ordinal))
            throw Reject("calendar_version_conflict", "Only a draft calendar version can be published.");
        if (string.Equals(record.CreatedByActorId, command.ActorId.Value, StringComparison.Ordinal))
            throw Reject("maker_cannot_approve_own_change", "The calendar maker cannot publish their own version.");

        bool overlaps = await _context.CalendarVersions.AnyAsync(x =>
            x.CalendarId == record.CalendarId && x.Status == "published" && x.VersionId != record.VersionId
            && x.EffectiveFrom <= record.EffectiveTo && record.EffectiveFrom <= x.EffectiveTo, cancellationToken).ConfigureAwait(false);
        if (overlaps)
            throw Reject("calendar_version_conflict", "The effective period overlaps an already published calendar version.");

        BusinessCalendarRecord calendar = await _context.BusinessCalendars
            .SingleAsync(x => x.CalendarId == record.CalendarId, cancellationToken).ConfigureAwait(false);
        calendar.Version++;

        BusinessDateControlRecord? control = await _context.BusinessDateControls.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (control is null)
        {
            if (record.InitialBusinessDate is not DateOnly initial)
                throw Reject("business_date_prerequisite_failed", "The first published calendar version must define an initial business date.");
            bool initialIsProcessing = await _context.CalendarDays.AnyAsync(x =>
                x.VersionId == record.VersionId && x.Date == initial && x.IsProcessingDay, cancellationToken).ConfigureAwait(false);
            if (!initialIsProcessing)
                throw Reject("business_date_prerequisite_failed", "The initial business date is not a processing day in this version.");
            control = new BusinessDateControlRecord
            {
                InstitutionId = command.InstitutionId.Value,
                CurrentBusinessDate = initial,
                CalendarVersionId = record.VersionId,
                UpdatedAt = _timeProvider.GetUtcNow(),
                Version = 1,
            };
            _context.BusinessDateControls.Add(control);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        record.Status = "published";
        record.ApprovedByActorId = command.ActorId.Value;
        record.ApprovalReference = command.ApprovalReference.Trim();
        record.ApprovalReason = command.Reason.Trim();
        record.PublishedAt = now;
        record.Version++;
        string eventId = AddOutbox(
            command.InstitutionId.Value,
            "business_calendar_published",
            record.CalendarId,
            command.CorrelationId,
            new BusinessCalendarPublishedV1(
                record.InstitutionId, record.CalendarId, record.VersionId, record.VersionNumber, record.TimeZoneId,
                record.EffectiveFrom, record.EffectiveTo, command.ActorId.Value, record.ApprovalReference, now),
            now);
        await SaveAsync("calendar_version_conflict", "The calendar version changed while publication was being processed.", cancellationToken).ConfigureAwait(false);
        return EncodeCalendar(record.InstitutionId, record.VersionId, eventId, "published", now);
    }

    private async Task<string?> RequestAdvanceCoreAsync(RequestBusinessDateAdvanceCommand command, CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(cancellationToken).ConfigureAwait(false);
        BusinessDateControlRecord control = await _context.BusinessDateControls.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw Reject("business_date_prerequisite_failed", "No business date has been initialised from an approved calendar.");
        CalendarVersionRecord calendar = await RequirePublishedCalendarAsync(command.CalendarVersionId, cancellationToken).ConfigureAwait(false);
        if (control.CurrentBusinessDate != command.ExpectedCurrentDate)
            throw Reject("business_date_transition_invalid", "The expected business date does not match the authoritative current date.");
        DateOnly next = await NextProcessingDateAsync(calendar.VersionId, control.CurrentBusinessDate, cancellationToken).ConfigureAwait(false);
        if (next != command.NewBusinessDate)
            throw Reject("business_date_transition_invalid", "The requested date is not the next processing day in the approved calendar.");

        DateTimeOffset now = _timeProvider.GetUtcNow();
        _context.BusinessDateAdvanceRequests.Add(new BusinessDateAdvanceRequestRecord
        {
            InstitutionId = command.InstitutionId.Value,
            RequestId = command.RequestId.Trim(),
            ExpectedCurrentDate = command.ExpectedCurrentDate,
            NewBusinessDate = command.NewBusinessDate,
            CalendarVersionId = calendar.VersionId,
            ProcessingCheckpointId = command.ProcessingCheckpointId.Trim(),
            Reason = command.Reason.Trim(),
            Status = "pending_approval",
            RequestedByActorId = command.ActorId.Value,
            CorrelationId = command.CorrelationId,
            RequestedAt = now,
            Version = 1,
        });
        await SaveAsync("business_date_transition_invalid", "The business-date request identifier is already in use.", cancellationToken).ConfigureAwait(false);
        return EncodeBusinessDate(command.InstitutionId.Value, command.RequestId.Trim(), command.NewBusinessDate, null, "pending_approval", now);
    }

    private async Task<string?> ApproveAdvanceCoreAsync(ApproveBusinessDateAdvanceCommand command, CancellationToken cancellationToken)
    {
        await EnsureInstitutionActiveAsync(cancellationToken).ConfigureAwait(false);
        BusinessDateAdvanceRequestRecord request = await _context.BusinessDateAdvanceRequests
            .SingleOrDefaultAsync(x => x.RequestId == command.RequestId, cancellationToken).ConfigureAwait(false)
            ?? throw Reject("business_date_transition_invalid", "The business-date request is not available in this institution.");
        if (!string.Equals(request.Status, "pending_approval", StringComparison.Ordinal))
            throw Reject("business_date_transition_invalid", "Only a pending business-date request can be approved.");
        BusinessDateControlRecord control = await _context.BusinessDateControls.SingleAsync(cancellationToken).ConfigureAwait(false);
        CalendarVersionRecord calendar = await RequirePublishedCalendarAsync(request.CalendarVersionId, cancellationToken).ConfigureAwait(false);
        DateOnly next = await NextProcessingDateAsync(calendar.VersionId, control.CurrentBusinessDate, cancellationToken).ConfigureAwait(false);
        int skipped;
        try
        {
            skipped = BusinessDatePolicy.ValidateAdvance(
                control.CurrentBusinessDate, request.ExpectedCurrentDate, request.NewBusinessDate, next,
                ActorId.From(request.RequestedByActorId), command.ActorId);
        }
        catch (InvalidOperationException exception)
        {
            string code = request.RequestedByActorId == command.ActorId.Value
                ? "maker_cannot_approve_own_change"
                : "business_date_transition_invalid";
            throw Reject(code, exception.Message);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly previous = control.CurrentBusinessDate;
        control.CurrentBusinessDate = request.NewBusinessDate;
        control.CalendarVersionId = calendar.VersionId;
        control.LastAdvanceRequestId = request.RequestId;
        control.UpdatedAt = now;
        control.Version++;
        request.Status = "executed";
        request.ApprovedByActorId = command.ActorId.Value;
        request.ApprovalReference = command.ApprovalReference.Trim();
        request.ApprovedAt = now;
        request.Version++;
        _context.BusinessDateHistory.Add(new BusinessDateHistoryRecord
        {
            InstitutionId = request.InstitutionId,
            HistoryId = Guid.NewGuid().ToString("N"),
            RequestId = request.RequestId,
            PreviousBusinessDate = previous,
            BusinessDate = request.NewBusinessDate,
            SkippedNonProcessingDays = skipped,
            CalendarVersionId = calendar.VersionId,
            ProcessingCheckpointId = request.ProcessingCheckpointId,
            RequestedByActorId = request.RequestedByActorId,
            ApprovedByActorId = command.ActorId.Value,
            ApprovalReference = request.ApprovalReference,
            Reason = request.Reason,
            CorrelationId = command.CorrelationId,
            AdvancedAt = now,
        });
        string eventId = AddOutbox(
            request.InstitutionId,
            "business_date_advanced",
            request.InstitutionId,
            command.CorrelationId,
            new BusinessDateAdvancedV1(
                request.InstitutionId, request.RequestId, previous, request.NewBusinessDate, skipped,
                calendar.VersionId, request.ProcessingCheckpointId, request.RequestedByActorId,
                command.ActorId.Value, request.ApprovalReference, now),
            now);
        await SaveAsync("business_date_transition_invalid", "The business date changed while approval was being processed.", cancellationToken).ConfigureAwait(false);
        return EncodeBusinessDate(request.InstitutionId, request.RequestId, request.NewBusinessDate, eventId, "executed", now);
    }

    private async Task EnsureInstitutionActiveAsync(CancellationToken cancellationToken)
    {
        InstitutionRecord? institution = await _context.Institutions.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (institution is null || !string.Equals(institution.Status, "active", StringComparison.Ordinal))
            throw Reject("institution_inactive", "Calendar and business-date changes require an active institution.");
    }

    private async Task<CalendarVersionRecord> RequirePublishedCalendarAsync(string versionId, CancellationToken cancellationToken)
    {
        CalendarVersionRecord? calendar = await _context.CalendarVersions
            .SingleOrDefaultAsync(x => x.VersionId == versionId && x.Status == "published", cancellationToken).ConfigureAwait(false);
        return calendar ?? throw Reject("business_date_prerequisite_failed", "The requested calendar version is not published.");
    }

    private async Task<DateOnly> NextProcessingDateAsync(string versionId, DateOnly currentDate, CancellationToken cancellationToken)
    {
        DateOnly? next = await _context.CalendarDays
            .Where(x => x.VersionId == versionId && x.Date > currentDate && x.IsProcessingDay)
            .OrderBy(x => x.Date)
            .Select(x => (DateOnly?)x.Date)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return next ?? throw Reject("business_date_prerequisite_failed", "The approved calendar has no later processing day.");
    }

    private void ValidateTarget(InstitutionId institutionId, string idempotencyKey, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        if (_tenantContext.CurrentInstitution != institutionId)
            throw Reject("cross_tenant_access_denied", "The authenticated institution does not match the calendar target.");
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
    }

    private string AddOutbox<T>(string institutionId, string eventType, string businessReference, string correlationId, T payload, DateTimeOffset now)
    {
        string eventId = Guid.NewGuid().ToString("N");
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = eventType,
            SchemaVersion = SchemaVersion,
            OccurredAt = now,
            InstitutionId = institutionId,
            CorrelationId = correlationId,
            Producer = Producer,
            BusinessReference = businessReference,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = institutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = eventType,
            SchemaVersion = SchemaVersion,
            Topic = EventContract.TopicFor(Producer, EventFamily, SchemaVersion),
            BusinessReference = businessReference,
            CorrelationId = correlationId,
            TraceParent = Activity.Current?.Id,
            TraceState = Activity.Current?.TraceStateString,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });
        return eventId;
    }

    private async Task SaveAsync(string code, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new BusinessCalendarRejectedException(code, message, exception);
        }
        catch (DbUpdateException exception)
        {
            throw new BusinessCalendarRejectedException(code, message, exception);
        }
    }

    private static BusinessCalendarRejectedException Reject(string code, string message) => new(code, message);

    private static string ToStorage(CalendarDayClassification classification) => classification switch
    {
        CalendarDayClassification.WorkingDay => "working_day",
        CalendarDayClassification.Holiday => "holiday",
        CalendarDayClassification.NonWorkingDay => "non_working_day",
        _ => throw new ArgumentOutOfRangeException(nameof(classification)),
    };

    private static CalendarCommandResult DecodeCalendarOutcome(IdempotencyOutcome outcome, bool replayed)
    {
        if (outcome.Kind == IdempotencyOutcomeKind.Conflict)
            throw Reject("idempotency_key_reused_with_different_request", "The idempotency key was already used with different calendar data.");
        if (outcome.Kind == IdempotencyOutcomeKind.InProgress)
            throw new BusinessCalendarInProgressException("An equivalent calendar request is still in progress.");
        string[] parts = (outcome.Result ?? string.Empty).Split('|');
        if (parts.Length != 5 || !DateTimeOffset.TryParseExact(parts[4], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset changedAt))
            throw new InvalidOperationException("The recorded idempotency outcome is not a calendar outcome.");
        return new CalendarCommandResult(parts[0], parts[1], EmptyToNull(parts[2]), parts[3], changedAt, replayed);
    }

    private static BusinessDateAdvanceResult DecodeBusinessDateOutcome(IdempotencyOutcome outcome, bool replayed)
    {
        if (outcome.Kind == IdempotencyOutcomeKind.Conflict)
            throw Reject("idempotency_key_reused_with_different_request", "The idempotency key was already used with different business-date data.");
        if (outcome.Kind == IdempotencyOutcomeKind.InProgress)
            throw new BusinessCalendarInProgressException("An equivalent business-date request is still in progress.");
        string[] parts = (outcome.Result ?? string.Empty).Split('|');
        if (parts.Length != 6
            || !DateOnly.TryParseExact(parts[2], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly businessDate)
            || !DateTimeOffset.TryParseExact(parts[5], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset changedAt))
            throw new InvalidOperationException("The recorded idempotency outcome is not a business-date outcome.");
        return new BusinessDateAdvanceResult(parts[0], parts[1], businessDate, EmptyToNull(parts[3]), parts[4], changedAt, replayed);
    }

    private static string EncodeCalendar(string institutionId, string resourceId, string? eventId, string status, DateTimeOffset changedAt) =>
        string.Join('|', institutionId, resourceId, eventId ?? string.Empty, status, changedAt.ToString("O", CultureInfo.InvariantCulture));

    private static string EncodeBusinessDate(string institutionId, string requestId, DateOnly businessDate, string? eventId, string status, DateTimeOffset changedAt) =>
        string.Join('|', institutionId, requestId, businessDate.ToString("O", CultureInfo.InvariantCulture), eventId ?? string.Empty, status, changedAt.ToString("O", CultureInfo.InvariantCulture));

    private static string? EmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;
}
