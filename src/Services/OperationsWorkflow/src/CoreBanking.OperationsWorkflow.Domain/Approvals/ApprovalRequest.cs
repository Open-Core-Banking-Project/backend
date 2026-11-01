using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.OperationsWorkflow.Domain.Approvals;

/// <summary>
/// The first approved profile. The constants delegate to the profile catalogue so the hash the source service
/// computes and the hash this service enforces cannot drift apart.
/// </summary>
public static class SavingsEligibilityPublicationProfile
{
    public const string WorkflowKey = "product.savings.eligibility.publish.v1";
    public const string WorkflowVersionId = "savings-eligibility-publication-v1";
    public const string SubjectType = "product.savings.eligibility.version";
    public const string SourceService = "core-ledger";
    public const string RequiredPermission = "product.version.approve";

    public static string DefinitionHash => ApprovalProfiles.SavingsEligibilityPublication.DefinitionHash;
}

public enum ApprovalRequestStatus
{
    Submitted,
    Approved,
    Rejected,
}

public enum ApprovalDecisionType
{
    Approve,
    Reject,
}

public sealed record EntitlementDecisionEvidence
{
    private EntitlementDecisionEvidence(
        InstitutionId institutionId,
        ActorId actorId,
        string permission,
        string entitlementVersion,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil)
    {
        InstitutionId = institutionId;
        ActorId = actorId;
        Permission = permission;
        EntitlementVersion = entitlementVersion;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
    }

    public InstitutionId InstitutionId { get; }
    public ActorId ActorId { get; }
    public string Permission { get; }
    public string EntitlementVersion { get; }
    public DateTimeOffset ValidFrom { get; }
    public DateTimeOffset ValidUntil { get; }

    public static EntitlementDecisionEvidence Create(
        InstitutionId institutionId,
        ActorId actorId,
        string permission,
        string entitlementVersion,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        ArgumentException.ThrowIfNullOrWhiteSpace(entitlementVersion);
        if (validUntil <= validFrom)
            throw new ArgumentException("Entitlement validity must end after it begins.", nameof(validUntil));
        return new EntitlementDecisionEvidence(
            institutionId, actorId, permission.Trim(), entitlementVersion.Trim(), validFrom, validUntil);
    }

    public bool IsCurrentAt(DateTimeOffset instant) => instant >= ValidFrom && instant < ValidUntil;
}

public sealed record ApprovalDecision(
    string DecisionId,
    ApprovalDecisionType Decision,
    ActorId Approver,
    string Permission,
    string EntitlementVersion,
    DateTimeOffset EntitlementValidFrom,
    DateTimeOffset EntitlementValidUntil,
    string? Reason,
    DateTimeOffset DecidedAt);

public sealed class ApprovalRequest
{
    private ApprovalRequest(
        ApprovalProfile profile,
        InstitutionId institutionId,
        string requestId,
        string sourceRequestKey,
        string subjectId,
        string subjectVersion,
        string subjectHash,
        ActorId maker,
        string correlationId,
        DateTimeOffset submittedAt)
    {
        InstitutionId = institutionId;
        RequestId = requestId;
        SourceRequestKey = sourceRequestKey;
        WorkflowKey = profile.WorkflowKey;
        WorkflowVersionId = profile.WorkflowVersionId;
        WorkflowDefinitionHash = profile.DefinitionHash;
        SourceService = profile.SourceService;
        SubjectType = profile.SubjectType;
        SubjectId = subjectId;
        SubjectVersion = subjectVersion;
        SubjectHash = subjectHash;
        Maker = maker;
        RequiredPermission = profile.RequiredPermission;
        CorrelationId = correlationId;
        SubmittedAt = submittedAt;
    }

    public InstitutionId InstitutionId { get; }
    public string RequestId { get; }
    public string SourceRequestKey { get; }
    public string WorkflowKey { get; }
    public string WorkflowVersionId { get; }
    public string WorkflowDefinitionHash { get; }
    public string SourceService { get; }
    public string SubjectType { get; }
    public string SubjectId { get; }
    public string SubjectVersion { get; }
    public string SubjectHash { get; }
    public ActorId Maker { get; }
    public string RequiredPermission { get; }
    public string CorrelationId { get; }
    public DateTimeOffset SubmittedAt { get; }
    public ApprovalRequestStatus Status { get; private set; } = ApprovalRequestStatus.Submitted;
    public ApprovalDecision? Decision { get; private set; }

    /// <summary>The first approved profile, kept as a named factory for the Core Ledger flow.</summary>
    public static ApprovalRequest SubmitSavingsEligibilityPublication(
        InstitutionId institutionId,
        string requestId,
        string sourceRequestKey,
        string subjectId,
        string subjectVersion,
        string subjectHash,
        ActorId maker,
        string correlationId,
        DateTimeOffset submittedAt) =>
        Submit(
            ApprovalProfiles.SavingsEligibilityPublication,
            institutionId,
            requestId,
            sourceRequestKey,
            subjectId,
            subjectVersion,
            subjectHash,
            maker,
            correlationId,
            submittedAt);

    /// <summary>
    /// Submits a request pinned to one approved profile and to the exact subject version and hash the approver will
    /// see. The profile decides the workflow key, version, definition hash, source service, subject type and
    /// required permission; a caller cannot supply them.
    /// </summary>
    public static ApprovalRequest Submit(
        ApprovalProfile profile,
        InstitutionId institutionId,
        string requestId,
        string sourceRequestKey,
        string subjectId,
        string subjectVersion,
        string subjectHash,
        ActorId maker,
        string correlationId,
        DateTimeOffset submittedAt)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(maker);
        Require(requestId, nameof(requestId));
        Require(sourceRequestKey, nameof(sourceRequestKey));
        Require(subjectId, nameof(subjectId));
        Require(subjectVersion, nameof(subjectVersion));
        Require(correlationId, nameof(correlationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectHash);
        if (subjectHash.Length != 64 || !subjectHash.All(Uri.IsHexDigit))
            throw new ArgumentException("Subject hash must be a SHA-256 hexadecimal digest.", nameof(subjectHash));
        return new ApprovalRequest(
            profile,
            institutionId, requestId.Trim(), sourceRequestKey.Trim(), subjectId.Trim(), subjectVersion.Trim(),
            subjectHash.ToLowerInvariant(), maker, correlationId.Trim(), submittedAt);
    }

    public ApprovalDecision Decide(
        string decisionId,
        ApprovalDecisionType decision,
        EntitlementDecisionEvidence entitlement,
        string? reason,
        DateTimeOffset decidedAt)
    {
        Require(decisionId, nameof(decisionId));
        ArgumentNullException.ThrowIfNull(entitlement);
        if (Status != ApprovalRequestStatus.Submitted)
            throw new InvalidOperationException("approval_request_already_decided");
        if (entitlement.InstitutionId != InstitutionId)
            throw new InvalidOperationException("approval_entitlement_wrong_institution");
        if (entitlement.ActorId == Maker)
            throw new InvalidOperationException("maker_cannot_approve_own_request");
        if (!string.Equals(entitlement.Permission, RequiredPermission, StringComparison.Ordinal))
            throw new InvalidOperationException("approval_permission_missing");
        if (!entitlement.IsCurrentAt(decidedAt))
            throw new InvalidOperationException("approval_entitlement_stale");
        string? normalisedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (decision == ApprovalDecisionType.Reject && normalisedReason is null)
            throw new InvalidOperationException("approval_rejection_reason_required");

        Decision = new ApprovalDecision(
            decisionId.Trim(), decision, entitlement.ActorId, entitlement.Permission,
            entitlement.EntitlementVersion, entitlement.ValidFrom, entitlement.ValidUntil,
            normalisedReason, decidedAt);
        Status = decision == ApprovalDecisionType.Approve
            ? ApprovalRequestStatus.Approved
            : ApprovalRequestStatus.Rejected;
        return Decision;
    }

    private static void Require(string value, string parameterName) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
}
