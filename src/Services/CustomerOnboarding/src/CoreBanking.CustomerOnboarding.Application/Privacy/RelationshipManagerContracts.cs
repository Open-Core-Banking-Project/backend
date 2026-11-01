using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CustomerOnboarding.Application.Privacy;

/// <summary>
/// Assigns a relationship manager to a customer for an effective period. Reassignment closes the current
/// assignment and appends the next; the prior assignment is retained. Requirement: CUS-010, CUS-026.
/// </summary>
public sealed record AssignRelationshipManagerCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CustomerId,
    string ManagerActorId,
    string? ScopeBranchId,
    string? Reason,
    string CorrelationId);

public sealed record GetRelationshipManagerQuery(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CustomerId);

public sealed record RelationshipManagerAssignmentView(
    string AssignmentId,
    string CustomerId,
    string ManagerActorId,
    string? ScopeBranchId,
    string? PreviousAssignmentId,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string AssignedBy,
    string? Reason);

/// <summary>The current assignment plus every closed assignment, newest first.</summary>
public sealed record RelationshipManagerView(
    string CustomerId,
    RelationshipManagerAssignmentView? Current,
    IReadOnlyList<RelationshipManagerAssignmentView> History);

public interface IRelationshipManagerService
{
    Task<RelationshipManagerView> AssignAsync(AssignRelationshipManagerCommand command, CancellationToken cancellationToken);

    Task<RelationshipManagerView> GetAsync(GetRelationshipManagerQuery query, CancellationToken cancellationToken);
}
