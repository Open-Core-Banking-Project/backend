using System;
using CoreBanking.BuildingBlocks.Application.Persistence;
using CoreBanking.IdentityAccess.Domain.Authentication;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Authentication;

/// <summary>Immutable, institution-owned policy version metadata; security floors are external configuration.</summary>
public sealed class AuthenticationPolicyVersionRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string AuthenticationPolicyVersionId { get; set; }

    public AuthenticationChannel Channel { get; set; }

    public required string Provider { get; set; }

    public AuthenticationPolicyStatus Status { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }

    public DateTimeOffset? EffectiveUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
