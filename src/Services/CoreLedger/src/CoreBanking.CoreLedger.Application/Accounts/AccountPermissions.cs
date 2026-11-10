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

    public static PermissionIdentifier ActivationCreate { get; } =
        PermissionIdentifier.Parse("account.activation.create");

    public static PermissionIdentifier SummaryView { get; } =
        PermissionIdentifier.Parse("account.summary.view");

    public static PermissionIdentifier StatementRequest { get; } =
        PermissionIdentifier.Parse("account.statement.request");

    public static PermissionIdentifier StatementRetrieve { get; } =
        PermissionIdentifier.Parse("account.statement.retrieve");

    public static PermissionRequirement SummaryViewRequirement { get; } = new()
    {
        Permission = SummaryView,
    };

    /// <summary>
    /// Authorising a statement request needs the account scope; the institution always comes from the validated
    /// token. A signed artifact reference is a separate, audited retrieval step. Decision: P1-E04-D15.
    /// </summary>
    public static PermissionRequirement StatementRequestRequirement { get; } = new()
    {
        Permission = StatementRequest,
        RequiresStepUp = true,
    };

    public static PermissionRequirement StatementRetrieveRequirement { get; } = new()
    {
        Permission = StatementRetrieve,
        RequiresStepUp = true,
    };

    public static PermissionIdentifier DormancyAssess { get; } =
        PermissionIdentifier.Parse("account.dormancy.assess");

    public static PermissionIdentifier DormancyReactivate { get; } =
        PermissionIdentifier.Parse("account.dormancy.reactivate");

    public static PermissionIdentifier ClosurePropose { get; } =
        PermissionIdentifier.Parse("account.closure.propose");

    public static PermissionIdentifier ClosureApprove { get; } =
        PermissionIdentifier.Parse("account.closure.approve");

    public static PermissionRequirement DormancyAssessRequirement { get; } = new()
    {
        Permission = DormancyAssess,
    };

    public static PermissionRequirement DormancyReactivateRequirement { get; } = new()
    {
        Permission = DormancyReactivate,
        RequiresStepUp = true,
    };

    public static PermissionRequirement ClosureProposeRequirement { get; } = new()
    {
        Permission = ClosurePropose,
        RequiresStepUp = true,
    };

    /// <summary>
    /// The closure approver decides a proposal under the <c>account.close.v1</c> profile and can never approve
    /// their own proposal. Decision: P1-E04-D13.
    /// </summary>
    public static PermissionRequirement ClosureApproveRequirement { get; } = new()
    {
        Permission = ClosureApprove,
        RequiresStepUp = true,
        IsRestrictedApproval = true,
    };

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

    public static PermissionIdentifier RestrictionApply { get; } =
        PermissionIdentifier.Parse("account.restriction.apply");

    public static PermissionIdentifier RestrictionRelease { get; } =
        PermissionIdentifier.Parse("account.restriction.release");

    /// <summary>
    /// The ledger's control-evaluation capability. The epic's four-segment
    /// <c>service.account.controls.evaluate</c> is not expressible under the grammar, so the approved
    /// three-segment form is used. Decision: P1-E04-D10.
    /// </summary>
    public static PermissionIdentifier ControlsEvaluate { get; } =
        PermissionIdentifier.Parse("account.controls.evaluate");

    public static PermissionIdentifier LienCreate { get; } =
        PermissionIdentifier.Parse("account.lien.create");

    public static PermissionIdentifier LienRelease { get; } =
        PermissionIdentifier.Parse("account.lien.release");

    public static PermissionRequirement LienCreateRequirement { get; } = new()
    {
        Permission = LienCreate,
        RequiresStepUp = true,
    };

    public static PermissionRequirement LienReleaseRequirement { get; } = new()
    {
        Permission = LienRelease,
        RequiresStepUp = true,
        IsRestrictedApproval = true,
    };

    public static PermissionRequirement RestrictionApplyRequirement { get; } = new()
    {
        Permission = RestrictionApply,
        RequiresStepUp = true,
    };

    public static PermissionRequirement RestrictionReleaseRequirement { get; } = new()
    {
        Permission = RestrictionRelease,
        RequiresStepUp = true,
    };

    public static PermissionRequirement ControlsEvaluateRequirement { get; } = new()
    {
        Permission = ControlsEvaluate,
    };

    public static PermissionRequirement NumberRevealRequirement { get; } = new()
    {
        Permission = NumberReveal,
        RequiresStepUp = true,
    };

    public static PermissionRequirement ActivationCreateRequirement { get; } = new()
    {
        Permission = ActivationCreate,
        RequiresStepUp = true,
    };
}
