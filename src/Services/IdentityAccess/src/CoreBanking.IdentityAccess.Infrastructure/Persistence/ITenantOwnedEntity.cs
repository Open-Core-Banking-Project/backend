namespace CoreBanking.IdentityAccess.Infrastructure.Persistence;

/// <summary>
/// Marks a table whose rows belong to exactly one institution. Every implementing entity is
/// required to carry a non-null institution column in its primary key, and receives the
/// mandatory institution query filter automatically.
/// Decision: docs/adr/ADR-0004-database-per-service-and-tenant-isolation.md
/// </summary>
public interface ITenantOwnedEntity
{
    string InstitutionId { get; }
}

/// <summary>Lifecycle of a stored identity, service client or role.</summary>
public enum RecordStatus
{
    Active = 1,
    Suspended = 2,
    Archived = 3,
}

/// <summary>Outcome recorded for an authentication attempt.</summary>
public enum AuthenticationOutcome
{
    Succeeded = 1,
    Failed = 2,
}
