using CoreBanking.BuildingBlocks.Application.Security;

namespace CoreBanking.CoreLedger.Application.Products;

public static class ProductPermissions
{
    public static PermissionIdentifier Update { get; } = PermissionIdentifier.Parse("product.version.update");

    public static PermissionIdentifier Simulate { get; } = PermissionIdentifier.Parse("product.version.simulate");

    public static PermissionIdentifier Publish { get; } = PermissionIdentifier.Parse("product.version.publish");

    public static PermissionRequirement UpdateRequirement { get; } = new()
    {
        Permission = Update,
        RequiresStepUp = true,
    };

    public static PermissionRequirement SimulateRequirement { get; } = new()
    {
        Permission = Simulate,
    };

    public static PermissionRequirement PublishRequirement { get; } = new()
    {
        Permission = Publish,
        RequiresStepUp = true,
        IsRestrictedApproval = true,
    };
}
