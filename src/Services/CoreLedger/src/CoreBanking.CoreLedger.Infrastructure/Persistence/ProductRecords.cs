using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

public sealed class ProductRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string ProductId { get; set; }
    public required string Code { get; set; }
    public required string Family { get; set; }
    public required string ConfigurationSetId { get; set; }
    public required string CreatedByActorId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
