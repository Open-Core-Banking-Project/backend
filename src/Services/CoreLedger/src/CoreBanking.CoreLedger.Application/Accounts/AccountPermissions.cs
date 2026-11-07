using CoreBanking.BuildingBlocks.Application.Security;

namespace CoreBanking.CoreLedger.Application.Accounts;

/// <summary>
/// Permissions for the account-application boundary, using the names approved by P1-E04-D09 because the
/// epic's two-segment names are not expressible under the three-segment grammar.
/// </summary>
public static class AccountPermissions
{
    public static PermissionIdentifier ApplicationCreate { get; } =
        PermissionIdentifier.Parse("account.application.create");

    public static PermissionIdentifier ApplicationView { get; } =
        PermissionIdentifier.Parse("account.application.view");

    public static PermissionIdentifier NumberReveal { get; } =
        PermissionIdentifier.Parse("account.number.reveal");

    public static PermissionRequirement ApplicationCreateRequirement { get; } = new()
    {
        Permission = ApplicationCreate,
        RequiresStepUp = true,
    };

    public static PermissionRequirement ApplicationApproveRequirement { get; } = new()
    {
        Permission = PermissionIdentifier.Parse("account.application.approve"),
        RequiresStepUp = true,
        IsRestrictedApproval = true,
    };

    public static PermissionRequirement ApplicationViewRequirement { get; } = new()
    {
        Permission = ApplicationView,
    };

    public static PermissionRequirement NumberRevealRequirement { get; } = new()
    {
        Permission = NumberReveal,
        RequiresStepUp = true,
    };
}
