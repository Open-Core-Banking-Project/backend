using System;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Institutions;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class BusinessCalendarTests
{
    private static readonly InstitutionId Institution = InstitutionId.From("institution-1");
    private static readonly ActorId Maker = ActorId.From("maker-1");

    [Fact]
    public void Calendar_version_requires_every_effective_date_to_be_classified()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => BusinessCalendarVersion.Create(
            Institution, "primary", "calendar-v1", 1, "Africa/Lagos",
            new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 19), Maker,
            [new CalendarDayDefinition(new DateOnly(2026, 10, 19), CalendarDayClassification.WorkingDay, true)]));

        Assert.Contains("classify every date", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Next_processing_date_uses_explicit_calendar_classification()
    {
        BusinessCalendarVersion version = CreateVersion();

        DateOnly next = version.NextProcessingDateAfter(new DateOnly(2026, 10, 19));

        Assert.Equal(new DateOnly(2026, 10, 21), next);
    }

    [Fact]
    public void Calendar_maker_cannot_publish_their_own_version()
    {
        BusinessCalendarVersion version = CreateVersion();

        Assert.Throws<InvalidOperationException>(() => version.EnsureIndependentPublisher(Maker));
    }

    [Fact]
    public void Business_date_advance_requires_independent_approver_and_exact_next_processing_date()
    {
        Assert.Throws<InvalidOperationException>(() => BusinessDatePolicy.ValidateAdvance(
            new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 21),
            new DateOnly(2026, 10, 21), Maker, Maker));

        Assert.Throws<InvalidOperationException>(() => BusinessDatePolicy.ValidateAdvance(
            new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 22),
            new DateOnly(2026, 10, 21), Maker, ActorId.From("approver-1")));

        int skipped = BusinessDatePolicy.ValidateAdvance(
            new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 21),
            new DateOnly(2026, 10, 21), Maker, ActorId.From("approver-1"));
        Assert.Equal(1, skipped);
    }

    private static BusinessCalendarVersion CreateVersion() => BusinessCalendarVersion.Create(
        Institution, "primary", "calendar-v1", 1, "Africa/Lagos",
        new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 19), Maker,
        [
            new CalendarDayDefinition(new DateOnly(2026, 10, 19), CalendarDayClassification.WorkingDay, true),
            new CalendarDayDefinition(new DateOnly(2026, 10, 20), CalendarDayClassification.Holiday, false),
            new CalendarDayDefinition(new DateOnly(2026, 10, 21), CalendarDayClassification.WorkingDay, true),
        ]);
}
