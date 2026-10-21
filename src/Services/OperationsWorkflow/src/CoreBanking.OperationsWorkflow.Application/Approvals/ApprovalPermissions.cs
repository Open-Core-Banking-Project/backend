using CoreBanking.BuildingBlocks.Application.Security;

namespace CoreBanking.OperationsWorkflow.Application.Approvals;

public static class ApprovalPermissions
{
    public static PermissionIdentifier Decide { get; } = PermissionIdentifier.Parse("product.version.approve");

    public static PermissionRequirement DecideRequirement { get; } = new()
    {
        Permission = Decide,
        RequiresStepUp = true,
        IsRestrictedApproval = true,
    };
}
