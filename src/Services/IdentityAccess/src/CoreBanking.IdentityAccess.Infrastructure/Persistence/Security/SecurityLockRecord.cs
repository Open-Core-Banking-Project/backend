using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;

/// <summary>Policy-driven lock evidence. Threshold decisions are not encoded in this record.</summary>
public sealed class SecurityLockRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }
    public required string SecurityLockId { get; set; }
    public required string IdentityId { get; set; }
    public required string Scope { get; set; }
    public required string ReasonCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
}
