using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Institutions;

namespace CoreBanking.CoreLedger.Application.Institutions;

public sealed record CreateCalendarVersionCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string CalendarId,
    string VersionId,
    int VersionNumber,
    string TimeZoneId,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    DateOnly? InitialBusinessDate,
    IReadOnlyCollection<CalendarDayDefinition> Days,
    string CorrelationId);

public sealed record PublishCalendarVersionCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string VersionId,
    string ApprovalReference,
    string Reason,
    string CorrelationId);

public sealed record RequestBusinessDateAdvanceCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string RequestId,
    DateOnly ExpectedCurrentDate,
    DateOnly NewBusinessDate,
    string CalendarVersionId,
    string ProcessingCheckpointId,
    string Reason,
    string CorrelationId);

public sealed record ApproveBusinessDateAdvanceCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string RequestId,
    string ApprovalReference,
    string CorrelationId);

public sealed record CalendarCommandResult(
    string InstitutionId,
    string ResourceId,
    string? EventId,
    string Status,
    DateTimeOffset ChangedAt,
    bool Replayed);

public sealed record BusinessDateAdvanceResult(
    string InstitutionId,
    string RequestId,
    DateOnly BusinessDate,
    string? EventId,
    string Status,
    DateTimeOffset ChangedAt,
    bool Replayed);

public interface IBusinessCalendarService
{
    Task<CalendarCommandResult> CreateVersionAsync(CreateCalendarVersionCommand command, CancellationToken cancellationToken);
    Task<CalendarCommandResult> PublishVersionAsync(PublishCalendarVersionCommand command, CancellationToken cancellationToken);
    Task<BusinessDateAdvanceResult> RequestAdvanceAsync(RequestBusinessDateAdvanceCommand command, CancellationToken cancellationToken);
    Task<BusinessDateAdvanceResult> ApproveAdvanceAsync(ApproveBusinessDateAdvanceCommand command, CancellationToken cancellationToken);
}

public sealed class BusinessCalendarRejectedException : Exception
{
    public BusinessCalendarRejectedException(string code, string message) : base(message) => Code = code;
    public BusinessCalendarRejectedException(string code, string message, Exception inner) : base(message, inner) => Code = code;
    public string Code { get; }
}

public sealed class BusinessCalendarInProgressException : Exception
{
    public BusinessCalendarInProgressException(string message) : base(message) { }
}
