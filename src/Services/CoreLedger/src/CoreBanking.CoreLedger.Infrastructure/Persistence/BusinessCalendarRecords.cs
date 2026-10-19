using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

public sealed class BusinessCalendarRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string CalendarId { get; set; }
    public required string Name { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CalendarVersionRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string VersionId { get; set; }
    public required string CalendarId { get; set; }
    public int VersionNumber { get; set; }
    public required string TimeZoneId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly EffectiveTo { get; set; }
    public DateOnly? InitialBusinessDate { get; set; }
    public required string Status { get; set; }
    public required string CreatedByActorId { get; set; }
    public string? ApprovedByActorId { get; set; }
    public string? ApprovalReference { get; set; }
    public string? ApprovalReason { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class CalendarDayRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string VersionId { get; set; }
    public DateOnly Date { get; set; }
    public required string Classification { get; set; }
    public bool IsProcessingDay { get; set; }
}

public sealed class BusinessDateControlRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public DateOnly CurrentBusinessDate { get; set; }
    public required string CalendarVersionId { get; set; }
    public string? LastAdvanceRequestId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class BusinessDateAdvanceRequestRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string RequestId { get; set; }
    public DateOnly ExpectedCurrentDate { get; set; }
    public DateOnly NewBusinessDate { get; set; }
    public required string CalendarVersionId { get; set; }
    public required string ProcessingCheckpointId { get; set; }
    public required string Reason { get; set; }
    public required string Status { get; set; }
    public required string RequestedByActorId { get; set; }
    public string? ApprovedByActorId { get; set; }
    public string? ApprovalReference { get; set; }
    public required string CorrelationId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public long Version { get; set; }
}

public sealed class BusinessDateHistoryRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string HistoryId { get; set; }
    public required string RequestId { get; set; }
    public DateOnly PreviousBusinessDate { get; set; }
    public DateOnly BusinessDate { get; set; }
    public int SkippedNonProcessingDays { get; set; }
    public required string CalendarVersionId { get; set; }
    public required string ProcessingCheckpointId { get; set; }
    public required string RequestedByActorId { get; set; }
    public required string ApprovedByActorId { get; set; }
    public required string ApprovalReference { get; set; }
    public required string Reason { get; set; }
    public required string CorrelationId { get; set; }
    public DateTimeOffset AdvancedAt { get; set; }
}
