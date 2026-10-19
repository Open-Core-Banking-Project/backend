using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Domain.Institutions;

public enum CalendarDayClassification
{
    WorkingDay,
    Holiday,
    NonWorkingDay,
}

public sealed record CalendarDayDefinition(
    DateOnly Date,
    CalendarDayClassification Classification,
    bool IsProcessingDay);

public sealed class BusinessCalendarVersion
{
    private readonly IReadOnlyDictionary<DateOnly, CalendarDayDefinition> _days;

    private BusinessCalendarVersion(
        InstitutionId institutionId,
        string calendarId,
        string versionId,
        int versionNumber,
        string timeZoneId,
        DateOnly effectiveFrom,
        DateOnly effectiveTo,
        DateOnly? initialBusinessDate,
        ActorId createdBy,
        IReadOnlyDictionary<DateOnly, CalendarDayDefinition> days)
    {
        InstitutionId = institutionId;
        CalendarId = calendarId;
        VersionId = versionId;
        VersionNumber = versionNumber;
        TimeZoneId = timeZoneId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        InitialBusinessDate = initialBusinessDate;
        CreatedBy = createdBy;
        _days = days;
    }

    public InstitutionId InstitutionId { get; }
    public string CalendarId { get; }
    public string VersionId { get; }
    public int VersionNumber { get; }
    public string TimeZoneId { get; }
    public DateOnly EffectiveFrom { get; }
    public DateOnly EffectiveTo { get; }
    public DateOnly? InitialBusinessDate { get; }
    public ActorId CreatedBy { get; }
    public IReadOnlyCollection<CalendarDayDefinition> Days => _days.Values.ToArray();

    public static BusinessCalendarVersion Create(
        InstitutionId institutionId,
        string calendarId,
        string versionId,
        int versionNumber,
        string timeZoneId,
        DateOnly effectiveFrom,
        DateOnly effectiveTo,
        DateOnly? initialBusinessDate,
        ActorId createdBy,
        IEnumerable<CalendarDayDefinition> days)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(calendarId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        ArgumentNullException.ThrowIfNull(createdBy);
        ArgumentNullException.ThrowIfNull(days);
        ArgumentOutOfRangeException.ThrowIfLessThan(versionNumber, 1);
        if (effectiveTo < effectiveFrom) throw new ArgumentException("The effective end date cannot precede the start date.", nameof(effectiveTo));

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new ArgumentException("The calendar timezone is not recognised by this deployment.", nameof(timeZoneId), exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new ArgumentException("The calendar timezone is invalid.", nameof(timeZoneId), exception);
        }

        Dictionary<DateOnly, CalendarDayDefinition> indexed = new();
        foreach (CalendarDayDefinition day in days)
        {
            if (day.Date < effectiveFrom || day.Date > effectiveTo)
                throw new ArgumentException("Every calendar day must fall inside the effective period.", nameof(days));
            if (!indexed.TryAdd(day.Date, day))
                throw new ArgumentException($"Calendar date {day.Date:yyyy-MM-dd} is duplicated.", nameof(days));
        }

        int expectedDayCount = effectiveTo.DayNumber - effectiveFrom.DayNumber + 1;
        if (indexed.Count != expectedDayCount)
            throw new ArgumentException("The calendar version must classify every date in its effective period.", nameof(days));
        if (initialBusinessDate is DateOnly initial
            && (!indexed.TryGetValue(initial, out CalendarDayDefinition? initialDay) || !initialDay.IsProcessingDay))
            throw new ArgumentException("The initial business date must be a processing day in this version.", nameof(initialBusinessDate));

        return new BusinessCalendarVersion(
            institutionId,
            calendarId.Trim(),
            versionId.Trim(),
            versionNumber,
            timeZoneId.Trim(),
            effectiveFrom,
            effectiveTo,
            initialBusinessDate,
            createdBy,
            indexed);
    }

    public DateOnly NextProcessingDateAfter(DateOnly currentDate)
    {
        CalendarDayDefinition? next = _days.Values
            .Where(day => day.Date > currentDate && day.IsProcessingDay)
            .OrderBy(day => day.Date)
            .FirstOrDefault();
        return next?.Date
            ?? throw new InvalidOperationException("No later processing day exists in the approved calendar version.");
    }

    public void EnsureIndependentPublisher(ActorId publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        if (publisher == CreatedBy)
            throw new InvalidOperationException("The calendar maker cannot publish their own version.");
    }
}

public static class BusinessDatePolicy
{
    public static int ValidateAdvance(
        DateOnly currentDate,
        DateOnly expectedCurrentDate,
        DateOnly requestedDate,
        DateOnly nextProcessingDate,
        ActorId requestedBy,
        ActorId approvedBy)
    {
        ArgumentNullException.ThrowIfNull(requestedBy);
        ArgumentNullException.ThrowIfNull(approvedBy);
        if (requestedBy == approvedBy)
            throw new InvalidOperationException("The business-date maker cannot approve their own request.");
        if (currentDate != expectedCurrentDate)
            throw new InvalidOperationException("The current business date changed after the request was created.");
        if (requestedDate <= currentDate)
            throw new InvalidOperationException("Business date must advance monotonically.");
        if (requestedDate != nextProcessingDate)
            throw new InvalidOperationException("The requested date is not the next processing day in the approved calendar.");
        return requestedDate.DayNumber - currentDate.DayNumber - 1;
    }
}
