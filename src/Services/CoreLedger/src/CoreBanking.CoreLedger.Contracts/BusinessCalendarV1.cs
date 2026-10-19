using System;
using System.Collections.Generic;

namespace CoreBanking.CoreLedger.Contracts;

public sealed record CalendarDayV1(DateOnly Date, string Classification, bool IsProcessingDay);
public sealed record CreateCalendarVersionRequestV1(
    string CalendarId,
    string VersionId,
    int VersionNumber,
    string TimeZoneId,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    DateOnly? InitialBusinessDate,
    IReadOnlyCollection<CalendarDayV1> Days);
public sealed record PublishCalendarVersionRequestV1(string ApprovalReference, string Reason);
public sealed record RequestBusinessDateAdvanceV1(
    string RequestId,
    DateOnly ExpectedCurrentDate,
    DateOnly NewBusinessDate,
    string CalendarVersionId,
    string ProcessingCheckpointId,
    string Reason);
public sealed record ApproveBusinessDateAdvanceV1(string ApprovalReference);
public sealed record CalendarCommandResponseV1(string InstitutionId, string ResourceId, string? EventId, string Status, DateTimeOffset ChangedAt, bool Replayed);
public sealed record BusinessDateAdvanceResponseV1(string InstitutionId, string RequestId, DateOnly BusinessDate, string? EventId, string Status, DateTimeOffset ChangedAt, bool Replayed);

public sealed record BusinessCalendarPublishedV1(
    string InstitutionId,
    string CalendarId,
    string VersionId,
    int VersionNumber,
    string TimeZoneId,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    string ApprovedByActorId,
    string ApprovalReference,
    DateTimeOffset PublishedAt);

public sealed record BusinessDateAdvancedV1(
    string InstitutionId,
    string RequestId,
    DateOnly PreviousBusinessDate,
    DateOnly BusinessDate,
    int SkippedNonProcessingDays,
    string CalendarVersionId,
    string ProcessingCheckpointId,
    string RequestedByActorId,
    string ApprovedByActorId,
    string ApprovalReference,
    DateTimeOffset AdvancedAt);
