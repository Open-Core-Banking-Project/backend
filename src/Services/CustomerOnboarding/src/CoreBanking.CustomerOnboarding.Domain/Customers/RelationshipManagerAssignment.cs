using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CustomerOnboarding.Domain.Customers;

/// <summary>
/// One effective-dated staff relationship. Reassignment closes the current assignment and appends the next, so
/// the prior manager stays in history and a customer has at most one current assignment.
/// Requirement: CUS-010, CUS-026.
/// </summary>
public sealed class RelationshipManagerAssignment
{
    private RelationshipManagerAssignment(
        InstitutionId institutionId,
        string assignmentId,
        string customerId,
        string managerActorId,
        string? scopeBranchId,
        string? previousAssignmentId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        string assignedBy,
        string? reason)
    {
        InstitutionId = institutionId;
        AssignmentId = assignmentId;
        CustomerId = customerId;
        ManagerActorId = managerActorId;
        ScopeBranchId = scopeBranchId;
        PreviousAssignmentId = previousAssignmentId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        AssignedBy = assignedBy;
        Reason = reason;
    }

    public InstitutionId InstitutionId { get; }

    public string AssignmentId { get; }

    public string CustomerId { get; }

    /// <summary>Stable staff identity reference; this service stores no staff personal data.</summary>
    public string ManagerActorId { get; }

    /// <summary>Optional branch scope supplied by the caller; it never widens who may be assigned.</summary>
    public string? ScopeBranchId { get; }

    public string? PreviousAssignmentId { get; }

    public DateTimeOffset EffectiveFrom { get; }

    public DateTimeOffset? EffectiveTo { get; private set; }

    public string AssignedBy { get; }

    public string? Reason { get; }

    public bool IsCurrent => EffectiveTo is null;

    public static RelationshipManagerAssignment Assign(
        InstitutionId institutionId,
        string customerId,
        string managerActorId,
        string? scopeBranchId,
        string? previousAssignmentId,
        string assignedBy,
        string? reason,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        return new RelationshipManagerAssignment(
            institutionId,
            Guid.NewGuid().ToString("N"),
            customerId,
            StableCode(managerActorId, "managerActorId", 128, "customer_relationship_manager_invalid"),
            OptionalStableCode(scopeBranchId, "scopeBranchId", 64, "customer_relationship_manager_scope_invalid"),
            string.IsNullOrWhiteSpace(previousAssignmentId) ? null : previousAssignmentId.Trim(),
            now,
            effectiveTo: null,
            StableCode(assignedBy, "assignedBy", 128, "customer_relationship_manager_invalid"),
            OptionalText(reason, 500));
    }

    public static RelationshipManagerAssignment Restore(
        InstitutionId institutionId,
        string assignmentId,
        string customerId,
        string managerActorId,
        string? scopeBranchId,
        string? previousAssignmentId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        string assignedBy,
        string? reason) =>
        new(
            institutionId,
            assignmentId,
            customerId,
            managerActorId,
            scopeBranchId,
            previousAssignmentId,
            effectiveFrom,
            effectiveTo,
            assignedBy,
            reason);

    /// <summary>Closes the assignment. A closed assignment is never reopened.</summary>
    public void Close(DateTimeOffset now)
    {
        if (!IsCurrent)
        {
            throw new OnboardingRuleException(
                "customer_relationship_manager_closed",
                "A closed relationship-manager assignment cannot be closed again.");
        }

        if (now < EffectiveFrom)
        {
            throw new OnboardingRuleException(
                "customer_relationship_manager_period_invalid",
                "A relationship-manager assignment cannot end before it starts.");
        }

        EffectiveTo = now;
    }

    private static string StableCode(string? value, string field, int maxLength, string errorCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OnboardingRuleException(errorCode, $"A relationship-manager assignment requires {field}.");
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength || trimmed.Any(char.IsWhiteSpace))
        {
            throw new OnboardingRuleException(errorCode, $"A relationship-manager assignment {field} must be a stable code.");
        }

        return trimmed;
    }

    private static string? OptionalStableCode(string? value, string field, int maxLength, string errorCode) =>
        string.IsNullOrWhiteSpace(value) ? null : StableCode(value, field, maxLength, errorCode);

    private static string? OptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new OnboardingRuleException(
                "customer_relationship_manager_reason_invalid",
                "A relationship-manager assignment reason is too long.");
        }

        return trimmed;
    }
}
