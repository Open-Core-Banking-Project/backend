using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

/// <summary>
/// Append-only audit evidence for one security-relevant action. The application principal may only insert and
/// read this table; no update or delete path exists. Detail holds stable codes and references, never personal
/// data. Decision: P1-E03-D14.
/// </summary>
public sealed class CustomerAuditEventRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string AuditEventId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public string ActorId { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string SubjectType { get; set; } = string.Empty;

    public string SubjectReference { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;
}
