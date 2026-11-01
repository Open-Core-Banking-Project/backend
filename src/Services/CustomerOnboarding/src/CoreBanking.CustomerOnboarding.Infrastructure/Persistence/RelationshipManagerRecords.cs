using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

/// <summary>
/// One effective-dated relationship-manager assignment. Only one assignment per customer is current, and a
/// closed assignment is retained unchanged. Requirement: CUS-010, CUS-026.
/// </summary>
public sealed class RelationshipManagerAssignmentRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string AssignmentId { get; set; } = string.Empty;

    public string CustomerId { get; set; } = string.Empty;

    public string ManagerActorId { get; set; } = string.Empty;

    public string? ScopeBranchId { get; set; }

    public string? PreviousAssignmentId { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }

    public DateTimeOffset? EffectiveTo { get; set; }

    public string AssignedBy { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public CustomerRecord? Customer { get; set; }
}
