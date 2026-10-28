using CoreBanking.BuildingBlocks.Application.Security;

namespace CoreBanking.CustomerOnboarding.Application.Permissions;

/// <summary>
/// Permissions the onboarding use cases require. Capture is not step-up protected, because it is the
/// officer's normal work; disclosure and review permissions are decided with their own step instead
/// of being inferred here. Decision: epic 03 permission matrix.
/// </summary>
public static class CustomerOnboardingPermissions
{
    public static PermissionIdentifier ApplicationCreate { get; } = PermissionIdentifier.Parse("customer.application.create");

    public static PermissionIdentifier ApplicationUpdate { get; } = PermissionIdentifier.Parse("customer.application.update");

    public static PermissionIdentifier ApplicationSubmit { get; } = PermissionIdentifier.Parse("customer.application.submit");

    public static PermissionIdentifier ApplicationView { get; } = PermissionIdentifier.Parse("customer.application.view");

    public static PermissionIdentifier ApplicationReview { get; } = PermissionIdentifier.Parse("customer.application.review");

    public static PermissionRequirement ApplicationCreateRequirement { get; } = new() { Permission = ApplicationCreate };

    public static PermissionRequirement ApplicationUpdateRequirement { get; } = new() { Permission = ApplicationUpdate };

    public static PermissionRequirement ApplicationSubmitRequirement { get; } = new() { Permission = ApplicationSubmit };

    public static PermissionRequirement ApplicationViewRequirement { get; } = new() { Permission = ApplicationView };

    public static PermissionRequirement ApplicationReviewRequirement { get; } = new() { Permission = ApplicationReview };
}
