using System;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Application.Permissions;
using CoreBanking.IdentityAccess.Infrastructure.Authorisation;
using Microsoft.Extensions.DependencyInjection;

namespace CoreBanking.IdentityAccess.Api.Authorisation;

/// <summary>
/// Seeds synthetic development identities so the reference protected flow can be exercised
/// without an identity provider. The values are development-only and must never be used to
/// provision a real institution.
/// Decision: ADR-0008 approval gate and epic 03, which permit a replaceable local
/// implementation while the provider decision is open.
/// </summary>
public static class DevelopmentSeed
{
    public const string InstitutionValue = "dev-institution";

    public const string AdministratorsActorValue = "dev-admin";

    public const string OperatorActorValue = "dev-operator";

    public static SeedResult Apply(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        InMemoryEntitlementStore store = services.GetRequiredService<InMemoryEntitlementStore>();
        InstitutionId institution = InstitutionId.From(InstitutionValue);

        store.Grant(
            ActorId.From(AdministratorsActorValue),
            institution,
            IdentityPermissions.PlatformFoundationExecute,
            GrantScope.ForInstitution(institution));

        // The approver-eligibility query needs its own capability so the reference flow can be
        // exercised without inventing a production role bundle. Epic 02 slice 10.
        store.Grant(
            ActorId.From(AdministratorsActorValue),
            institution,
            IdentityPermissions.ApprovalEligibilityReview,
            GrantScope.ForInstitution(institution));

        // Self-scoped entitlement distribution, so the snapshot contract can be exercised locally.
        long administratorVersion = store.Grant(
            ActorId.From(AdministratorsActorValue),
            institution,
            IdentityPermissions.IdentityView,
            GrantScope.ForInstitution(institution));

        // An actor with no grants exists so a 403 can be demonstrated against a real identity.
        long operatorVersion = store.IncrementVersion(ActorId.From(OperatorActorValue), institution);

        return new SeedResult(institution.Value, AdministratorsActorValue, administratorVersion, OperatorActorValue, operatorVersion);
    }

    /// <summary>Identifiers and entitlement versions of the seeded identities. Contains no secrets.</summary>
    public sealed record SeedResult(
        string InstitutionId,
        string AdministratorsActorId,
        long AdministratorsEntitlementVersion,
        string OperatorActorId,
        long OperatorEntitlementVersion);
}
