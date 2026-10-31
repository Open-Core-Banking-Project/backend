using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Application.Authorisation;

namespace CoreBanking.IdentityAccess.Application.Approvals;

/// <summary>The evaluated answer, in application terms, before it becomes a version 1 contract.</summary>
public sealed record ApprovalEligibilityResult(
    bool IsEligible,
    DenialReason Reason,
    string Permission,
    bool MakerSeparated,
    long EvaluatedEntitlementVersion,
    DateTimeOffset EvaluatedAt);

/// <summary>
/// Answers "may this actor decide that request?" for an approval source without duplicating
/// authorisation rules. The question is evaluated by the same policy and evidence writer as every
/// protected operation, with segregation of duties switched on and the maker identity supplied by
/// the source.
/// Decision: ADR-0009; epic 02 slice 10.
/// </summary>
public sealed class ApprovalEligibilityService
{
    private const string OperationName = "identity.approval.eligibility.review";

    private readonly AuthorisationService _authorisation;
    private readonly TimeProvider _timeProvider;

    public ApprovalEligibilityService(AuthorisationService authorisation, TimeProvider timeProvider)
    {
        _authorisation = authorisation ?? throw new ArgumentNullException(nameof(authorisation));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ApprovalEligibilityResult> EvaluateAsync(
        PermissionIdentifier requiredPermission,
        string makerActorId,
        InstitutionId requestedInstitutionId,
        AuthenticatedActor? actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requiredPermission);
        ArgumentException.ThrowIfNullOrWhiteSpace(makerActorId);
        ArgumentNullException.ThrowIfNull(requestedInstitutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        AuthorisationRequest request = new()
        {
            Requirement = new PermissionRequirement
            {
                Permission = requiredPermission,
                IsRestrictedApproval = true,
            },
            Scope = ResourceScope.ForInstitution(requestedInstitutionId),
            Actor = actor,
            MakerActorId = makerActorId,
        };

        AuthorisationDecision decision = await _authorisation
            .AuthoriseAsync(request, OperationName, correlationId, cancellationToken)
            .ConfigureAwait(false);

        bool makerSeparated = actor is not null
            && !string.Equals(actor.Id.Value, makerActorId, StringComparison.Ordinal);

        return new ApprovalEligibilityResult(
            decision.IsAllowed,
            decision.Reason,
            requiredPermission.Value,
            makerSeparated,
            actor?.EntitlementVersion ?? 0,
            _timeProvider.GetUtcNow());
    }
}
