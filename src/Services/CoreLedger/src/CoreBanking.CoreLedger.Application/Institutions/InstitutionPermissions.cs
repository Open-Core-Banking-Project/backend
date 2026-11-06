using CoreBanking.BuildingBlocks.Application.Security;

namespace CoreBanking.CoreLedger.Application.Institutions;

public static class InstitutionPermissions
{
    public static PermissionIdentifier Create { get; } = PermissionIdentifier.Parse("platform.institution.create");

    public static PermissionIdentifier Activate { get; } = PermissionIdentifier.Parse("platform.institution.activate");

    public static PermissionIdentifier Suspend { get; } = PermissionIdentifier.Parse("platform.institution.suspend");

    public static PermissionIdentifier RegionManage { get; } = PermissionIdentifier.Parse("institution.region.manage");

    public static PermissionIdentifier BranchCreate { get; } = PermissionIdentifier.Parse("institution.branch.create");

    public static PermissionIdentifier BranchActivate { get; } = PermissionIdentifier.Parse("institution.branch.activate");

    public static PermissionIdentifier BranchSuspend { get; } = PermissionIdentifier.Parse("institution.branch.suspend");

    public static PermissionIdentifier CalendarManage { get; } = PermissionIdentifier.Parse("institution.calendar.manage");

    public static PermissionIdentifier BusinessDateAdvance { get; } = PermissionIdentifier.Parse("institution.business_date.advance");

    public static PermissionIdentifier ProvisioningRepair { get; } = PermissionIdentifier.Parse("platform.provisioning.repair");

    public static PermissionIdentifier ConfigurationCreate { get; } = PermissionIdentifier.Parse("institution.configuration.create");

    public static PermissionIdentifier ConfigurationSimulate { get; } = PermissionIdentifier.Parse("institution.configuration.simulate");

    public static PermissionIdentifier ConfigurationSubmit { get; } = PermissionIdentifier.Parse("institution.configuration.submit");

    public static PermissionIdentifier ConfigurationPublish { get; } = PermissionIdentifier.Parse("institution.configuration.publish");

    public static PermissionRequirement CreateRequirement { get; } = new()
    {
        Permission = Create,
        RequiresStepUp = true,
    };

    public static PermissionRequirement ActivateRequirement { get; } = new()
    {
        Permission = Activate,
        RequiresStepUp = true,
        IsRestrictedApproval = true,
    };

    public static PermissionRequirement SuspendRequirement { get; } = new()
    {
        Permission = Suspend,
        RequiresStepUp = true,
        IsRestrictedApproval = true,
    };

    public static PermissionRequirement RegionManageRequirement { get; } = new() { Permission = RegionManage };
    public static PermissionRequirement BranchCreateRequirement { get; } = new() { Permission = BranchCreate, RequiresStepUp = true };
    public static PermissionRequirement BranchActivateRequirement { get; } = new() { Permission = BranchActivate, RequiresStepUp = true, IsRestrictedApproval = true };
    public static PermissionRequirement BranchSuspendRequirement { get; } = new() { Permission = BranchSuspend, RequiresStepUp = true, IsRestrictedApproval = true };
    public static PermissionRequirement CalendarManageRequirement { get; } = new() { Permission = CalendarManage };
    public static PermissionRequirement CalendarPublishRequirement { get; } = new() { Permission = CalendarManage, RequiresStepUp = true, IsRestrictedApproval = true };
    public static PermissionRequirement BusinessDateAdvanceRequirement { get; } = new() { Permission = BusinessDateAdvance, RequiresStepUp = true, IsRestrictedApproval = true };
    public static PermissionRequirement ProvisioningRepairRequirement { get; } = new() { Permission = ProvisioningRepair, RequiresStepUp = true };

    public static PermissionRequirement ConfigurationCreateRequirement { get; } = new() { Permission = ConfigurationCreate, RequiresStepUp = true };
    public static PermissionRequirement ConfigurationSimulateRequirement { get; } = new() { Permission = ConfigurationSimulate, RequiresStepUp = true };
    public static PermissionRequirement ConfigurationSubmitRequirement { get; } = new() { Permission = ConfigurationSubmit, RequiresStepUp = true, IsRestrictedApproval = true };
    public static PermissionRequirement ConfigurationPublishRequirement { get; } = new() { Permission = ConfigurationPublish, RequiresStepUp = true, IsRestrictedApproval = true };
}
