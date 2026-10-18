namespace CoreBanking.BuildingBlocks.Application.Persistence;

/// <summary>
/// Marks a table whose rows belong to exactly one institution. The persistence layer requires an
/// implementing entity to carry a non-null institution column in its primary key and applies the
/// mandatory institution query filter to it.
/// Decision: docs/adr/ADR-0004-database-per-service-and-tenant-isolation.md
/// </summary>
public interface ITenantOwnedEntity
{
    string InstitutionId { get; }
}
