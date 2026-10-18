using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.BuildingBlocks.Application.Security;

/// <summary>
/// The reference authorisation evaluator. It denies by default, resolves nothing from
/// client-supplied claims, and returns a stable denial reason for every refusal.
/// Decision: docs/adr/ADR-0009-roles-permissions-contextual-scopes-and-invalidation.md
/// </summary>
public sealed class AuthorisationPolicy
{
    private readonly IEffectivePermissionSource _permissions;
    private readonly IEntitlementVersionSource _versions;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _snapshotTimeToLive;

    public AuthorisationPolicy(
        IEffectivePermissionSource permissions,
        IEntitlementVersionSource versions,
        TimeProvider timeProvider,
        TimeSpan snapshotTimeToLive)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (snapshotTimeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshotTimeToLive), "The snapshot lifetime must be positive.");
        }

        _snapshotTimeToLive = snapshotTimeToLive;
    }

    public AuthorisationDecision Evaluate(AuthorisationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        AuthenticatedActor? actor = request.Actor;
        if (actor is null)
        {
            return AuthorisationDecision.Denied(DenialReason.MissingAuthentication, "no authenticated actor was resolved", request.Requirement.Permission);
        }

        if (!request.Requirement.AllowedActorTypes.Contains(actor.Type))
        {
            return AuthorisationDecision.Denied(
                DenialReason.ServiceIdentityNotPermitted,
                $"actor type '{actor.Type}' may not satisfy '{request.Requirement.Permission.Value}'",
                request.Requirement.Permission);
        }

        ResourceScope scope = request.Scope;
        if (scope.InstitutionId is null || actor.InstitutionId != scope.InstitutionId)
        {
            return AuthorisationDecision.Denied(
                DenialReason.WrongInstitution,
                "the request institution does not match the authenticated context",
                request.Requirement.Permission);
        }

        long currentVersion = _versions.GetCurrentVersion(actor.Id, actor.InstitutionId);
        if (currentVersion != actor.EntitlementVersion)
        {
            return AuthorisationDecision.Denied(
                DenialReason.StaleEntitlement,
                "the entitlement version carried by the request is no longer current",
                request.Requirement.Permission);
        }

        EntitlementSnapshot? snapshot = _permissions.FindSnapshot(actor.Id, actor.InstitutionId);
        if (snapshot is null)
        {
            return AuthorisationDecision.Denied(
                DenialReason.MissingPermission,
                "no effective permissions are available for this actor",
                request.Requirement.Permission);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (snapshot.Version != actor.EntitlementVersion || now - snapshot.CapturedAt > _snapshotTimeToLive)
        {
            return AuthorisationDecision.Denied(
                DenialReason.StaleEntitlement,
                "the effective permission snapshot is stale and must be refreshed",
                request.Requirement.Permission);
        }

        if (request.Requirement.RequiresStepUp && (!request.StepUpSatisfied || actor.Strength < AuthenticationStrength.StepUp))
        {
            return AuthorisationDecision.Denied(
                DenialReason.StepUpRequired,
                "step-up authentication is required for this operation",
                request.Requirement.Permission);
        }

        PermissionGrant[] applicable = snapshot.Grants
            .Where(grant => grant.Permission == request.Requirement.Permission && grant.IsValidAt(now))
            .ToArray();

        if (applicable.Length == 0)
        {
            return AuthorisationDecision.Denied(
                DenialReason.MissingPermission,
                "the actor does not hold the required permission",
                request.Requirement.Permission);
        }

        if (!applicable.Any(grant => grant.Scope.Covers(scope)))
        {
            return AuthorisationDecision.Denied(
                DenialReason.ScopeMismatch,
                "the actor holds the permission outside the requested scope",
                request.Requirement.Permission);
        }

        if (request.Requirement.IsRestrictedApproval
            && request.MakerActorId is not null
            && string.Equals(request.MakerActorId, actor.Id.Value, StringComparison.Ordinal))
        {
            return AuthorisationDecision.Denied(
                DenialReason.SegregationOfDuties,
                "a maker cannot approve their own restricted action",
                request.Requirement.Permission);
        }

        return AuthorisationDecision.Allowed(request.Requirement.Permission);
    }
}
