using System;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.OperationsWorkflow.Application.Approvals;

namespace CoreBanking.OperationsWorkflow.Api.Authorisation;

public static class DevelopmentAuthorisationSeed
{
    public const string InstitutionValue = "dev-institution";
    public const string ApproverActorValue = "dev-product-approver";

    public static void Apply(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        LocalEntitlementStore store = (LocalEntitlementStore)services.GetService(typeof(LocalEntitlementStore))!;
        store.Grant(
            ActorId.From(ApproverActorValue),
            InstitutionId.From(InstitutionValue),
            ApprovalPermissions.Decide);
    }
}
