using System;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Institutions;

namespace CoreBanking.CoreLedger.Api.Authorisation;

public static class DevelopmentAuthorisationSeed
{
    public const string InstitutionValue = "dev-institution";
    public const string MakerActorValue = "dev-platform-maker";
    public const string ApproverActorValue = "dev-platform-approver";

    public static void Apply(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        LocalEntitlementStore store = (LocalEntitlementStore)services.GetService(typeof(LocalEntitlementStore))!;
        InstitutionId institutionId = InstitutionId.From(InstitutionValue);
        store.Grant(ActorId.From(MakerActorValue), institutionId, InstitutionPermissions.Create);
        store.Grant(ActorId.From(MakerActorValue), institutionId, InstitutionPermissions.CalendarManage);
        store.Grant(ActorId.From(MakerActorValue), institutionId, InstitutionPermissions.BusinessDateAdvance);
        store.Grant(ActorId.From(ApproverActorValue), institutionId, InstitutionPermissions.Activate);
        store.Grant(ActorId.From(ApproverActorValue), institutionId, InstitutionPermissions.CalendarManage);
        store.Grant(ActorId.From(ApproverActorValue), institutionId, InstitutionPermissions.BusinessDateAdvance);
    }
}
