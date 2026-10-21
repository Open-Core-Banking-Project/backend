using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.OperationsWorkflow.Application.Approvals;
using CoreBanking.OperationsWorkflow.Domain.Approvals;

namespace CoreBanking.OperationsWorkflow.Infrastructure.Approvals;

/// <summary>
/// Re-resolves the actor's effective grant and current entitlement version at decision time.
/// The supplied sources must be backed by the trusted Identity and Access integration; absence,
/// version drift, scope mismatch and expiry all fail closed.
/// </summary>
public sealed class CurrentEntitlementVerifier : IApprovalEntitlementVerifier
{
    private readonly IEffectivePermissionSource _permissions;
    private readonly IEntitlementVersionSource _versions;
    private readonly TimeSpan _maximumEvidenceAge;

    public CurrentEntitlementVerifier(
        IEffectivePermissionSource permissions,
        IEntitlementVersionSource versions,
        TimeSpan maximumEvidenceAge)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumEvidenceAge, TimeSpan.Zero);
        _maximumEvidenceAge = maximumEvidenceAge;
    }

    public Task<EntitlementDecisionEvidence?> VerifyAsync(
        InstitutionId institutionId,
        ActorId actorId,
        string requiredPermission,
        DateTimeOffset decisionTime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredPermission);
        EntitlementSnapshot? snapshot = _permissions.FindSnapshot(actorId, institutionId);
        long currentVersion = _versions.GetCurrentVersion(actorId, institutionId);
        if (snapshot is null
            || snapshot.ActorId != actorId
            || snapshot.InstitutionId != institutionId
            || snapshot.Version <= 0
            || snapshot.Version != currentVersion
            || snapshot.CapturedAt > decisionTime
            || decisionTime - snapshot.CapturedAt > _maximumEvidenceAge)
            return Task.FromResult<EntitlementDecisionEvidence?>(null);

        PermissionGrant? grant = snapshot.Grants.SingleOrDefault(candidate =>
            string.Equals(candidate.Permission.Value, requiredPermission, StringComparison.Ordinal)
            && candidate.Scope.InstitutionId == institutionId
            && candidate.IsValidAt(decisionTime));
        if (grant is null)
            return Task.FromResult<EntitlementDecisionEvidence?>(null);

        DateTimeOffset maximumValidUntil = snapshot.CapturedAt + _maximumEvidenceAge;
        DateTimeOffset validUntil = grant.ValidTo is DateTimeOffset grantExpiry && grantExpiry < maximumValidUntil
            ? grantExpiry
            : maximumValidUntil;
        if (decisionTime >= validUntil)
            return Task.FromResult<EntitlementDecisionEvidence?>(null);

        return Task.FromResult<EntitlementDecisionEvidence?>(EntitlementDecisionEvidence.Create(
            institutionId,
            actorId,
            requiredPermission,
            currentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            snapshot.CapturedAt,
            validUntil));
    }
}
