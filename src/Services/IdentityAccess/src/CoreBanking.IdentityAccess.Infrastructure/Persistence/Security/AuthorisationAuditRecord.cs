using System;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;

/// <summary>
/// Durable, minimal evidence for an authorisation decision. The application principal is granted
/// insert access only; this entity has no application update or delete path.
/// </summary>
public sealed class AuthorisationAuditRecord
{
    public required string AuditEventId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string? ActorId { get; set; }

    public string? ActorType { get; set; }

    public string? InstitutionId { get; set; }

    public required string Permission { get; set; }

    public required string Action { get; set; }

    public required string Decision { get; set; }

    public required string Reason { get; set; }

    public required string Source { get; set; }

    public required string CorrelationId { get; set; }

    public long? EntitlementVersion { get; set; }

    public string? SessionId { get; set; }

    public string? DeviceId { get; set; }

    public string? PolicyVersion { get; set; }
}
