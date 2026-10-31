using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CoreBanking.OperationsWorkflow.Domain.Approvals;

/// <summary>
/// One approved approval profile: the immutable key, version, definition hash, source service, subject type and
/// decision permission that a request must match. Decision: ADR-0016 and epic 06 slice 4.
/// </summary>
public sealed record ApprovalProfile(
    string WorkflowKey,
    string WorkflowVersionId,
    string DefinitionHash,
    string SourceService,
    string SubjectType,
    string RequiredPermission);

/// <summary>
/// The profiles this service accepts. A request whose profile is absent, or whose declared version, definition
/// hash, source service, subject type or permission differs from the approved profile, is refused before anything
/// is stored, so a source service cannot invent a workflow shape.
/// Decision: ADR-0016 (first profile), P1-E03-D09 (classification), P1-E03-D11 (merge).
/// </summary>
public static class ApprovalProfiles
{
    /// <summary>Every approved profile is a single-stage, one-level maker-checker flow with the maker excluded.</summary>
    public const string Pattern = "one-level";

    public const string MakerPolicy = "maker-excluded";

    public const string EntitlementPolicy = "decision-time-entitlement";

    public static ApprovalProfile SavingsEligibilityPublication { get; } = Create(
        "product.savings.eligibility.publish.v1",
        "savings-eligibility-publication-v1",
        "core-ledger",
        "product.savings.eligibility.version",
        "product.version.approve");

    public static ApprovalProfile CustomerClassification { get; } = Create(
        "customer.classification.approve.v1",
        "customer-classification-approval-v1",
        "customer-onboarding",
        "customer.classification.version",
        "customer.classification.approve");

    public static ApprovalProfile CustomerMerge { get; } = Create(
        "customer.merge.approve.v1",
        "customer-merge-approval-v1",
        "customer-onboarding",
        "customer.merge.request",
        "customer.merge.approve");

    public static IReadOnlyList<ApprovalProfile> All { get; } =
        [SavingsEligibilityPublication, CustomerClassification, CustomerMerge];

    /// <summary>The source services that may submit a request, derived from the approved profiles.</summary>
    public static IReadOnlyList<string> SourceServices { get; } =
        [.. All.Select(profile => profile.SourceService).Distinct(StringComparer.Ordinal)];

    public static ApprovalProfile? Find(string workflowKey) =>
        string.IsNullOrWhiteSpace(workflowKey)
            ? null
            : All.FirstOrDefault(profile => string.Equals(profile.WorkflowKey, workflowKey.Trim(), StringComparison.Ordinal));

    /// <summary>
    /// The definition hash pins the key, version, pattern, maker policy, permission and entitlement policy, so a
    /// change to any of them is a new profile rather than a silent edit.
    /// </summary>
    public static string ComputeDefinitionHash(
        string workflowKey,
        string workflowVersionId,
        string requiredPermission) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{workflowKey}|{workflowVersionId}|{Pattern}|{MakerPolicy}|{requiredPermission}|{EntitlementPolicy}")))
            .ToLowerInvariant();

    private static ApprovalProfile Create(
        string workflowKey,
        string workflowVersionId,
        string sourceService,
        string subjectType,
        string requiredPermission) =>
        new(
            workflowKey,
            workflowVersionId,
            ComputeDefinitionHash(workflowKey, workflowVersionId, requiredPermission),
            sourceService,
            subjectType,
            requiredPermission);
}
