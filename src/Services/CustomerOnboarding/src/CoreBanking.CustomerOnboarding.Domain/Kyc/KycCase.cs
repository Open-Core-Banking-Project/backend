using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CustomerOnboarding.Domain.Kyc;

/// <summary>Canonical verification outcomes. A provider result becomes one of these only through an approved, versioned mapping.</summary>
public enum CanonicalVerificationOutcome
{
    Passed,
    Failed,
    Inconclusive,
    ManualReview,
}

/// <summary>
/// KYC requirement lifecycle from the epic. Evidence can arrive before a check starts, and only a reviewer
/// can clear an inconclusive or manual-review item. Decision: epic 03 KYC lifecycle, P1-E03-D06.
/// </summary>
public enum KycRequirementStatus
{
    Required,
    EvidencePending,
    Ready,
    InProgress,
    Passed,
    Failed,
    Inconclusive,
    ManualReview,
    Waived,
}

/// <summary>Case state derived from its requirements: a case completes when every requirement is satisfied.</summary>
public enum KycCaseStatus
{
    Open,
    Completed,
}

/// <summary>Stable storage and wire codes for KYC enums.</summary>
public static class KycCodes
{
    public static string ToCode(CanonicalVerificationOutcome outcome) => outcome switch
    {
        CanonicalVerificationOutcome.Passed => "passed",
        CanonicalVerificationOutcome.Failed => "failed",
        CanonicalVerificationOutcome.Inconclusive => "inconclusive",
        CanonicalVerificationOutcome.ManualReview => "manual_review",
        _ => throw new OnboardingRuleException("kyc_outcome_invalid", $"Canonical outcome '{outcome}' has no code."),
    };

    public static CanonicalVerificationOutcome FromOutcomeCode(string code) => code switch
    {
        "passed" => CanonicalVerificationOutcome.Passed,
        "failed" => CanonicalVerificationOutcome.Failed,
        "inconclusive" => CanonicalVerificationOutcome.Inconclusive,
        "manual_review" => CanonicalVerificationOutcome.ManualReview,
        _ => throw new OnboardingRuleException("kyc_outcome_invalid", $"Canonical outcome code '{code}' is not known."),
    };

    public static string ToCode(KycRequirementStatus status) => status switch
    {
        KycRequirementStatus.Required => "required",
        KycRequirementStatus.EvidencePending => "evidence_pending",
        KycRequirementStatus.Ready => "ready",
        KycRequirementStatus.InProgress => "in_progress",
        KycRequirementStatus.Passed => "passed",
        KycRequirementStatus.Failed => "failed",
        KycRequirementStatus.Inconclusive => "inconclusive",
        KycRequirementStatus.ManualReview => "manual_review",
        KycRequirementStatus.Waived => "waived",
        _ => throw new OnboardingRuleException("kyc_requirement_status_invalid", $"Requirement status '{status}' has no code."),
    };

    public static KycRequirementStatus FromRequirementStatusCode(string code) => code switch
    {
        "required" => KycRequirementStatus.Required,
        "evidence_pending" => KycRequirementStatus.EvidencePending,
        "ready" => KycRequirementStatus.Ready,
        "in_progress" => KycRequirementStatus.InProgress,
        "passed" => KycRequirementStatus.Passed,
        "failed" => KycRequirementStatus.Failed,
        "inconclusive" => KycRequirementStatus.Inconclusive,
        "manual_review" => KycRequirementStatus.ManualReview,
        "waived" => KycRequirementStatus.Waived,
        _ => throw new OnboardingRuleException("kyc_requirement_status_invalid", $"Requirement status code '{code}' is not known."),
    };

    public static string ToCode(KycCaseStatus status) => status switch
    {
        KycCaseStatus.Open => "open",
        KycCaseStatus.Completed => "completed",
        _ => throw new OnboardingRuleException("kyc_case_status_invalid", $"Case status '{status}' has no code."),
    };

    public static KycCaseStatus FromCaseStatusCode(string code) => code switch
    {
        "open" => KycCaseStatus.Open,
        "completed" => KycCaseStatus.Completed,
        _ => throw new OnboardingRuleException("kyc_case_status_invalid", $"Case status code '{code}' is not known."),
    };
}

/// <summary>
/// One KYC requirement of a case. It cites a requirement snapshot entry and moves through the epic's
/// lifecycle; only a reviewer resolves an inconclusive or manual-review item and only an explicit
/// authority can waive one. Decision: epic 03 KYC lifecycle, P1-E03-D06.
/// </summary>
public sealed class KycRequirement
{
    private const int MaximumReasonLength = 500;

    private KycRequirement(
        string code,
        KycRequirementStatus status,
        string? linkedVerificationId,
        string? waiverAuthority,
        string? waiverReason,
        DateTimeOffset? waivedAt,
        DateTimeOffset updatedAt)
    {
        Code = code;
        Status = status;
        LinkedVerificationId = linkedVerificationId;
        WaiverAuthority = waiverAuthority;
        WaiverReason = waiverReason;
        WaivedAt = waivedAt;
        UpdatedAt = updatedAt;
    }

    public string Code { get; }

    public KycRequirementStatus Status { get; private set; }

    public string? LinkedVerificationId { get; private set; }

    public string? WaiverAuthority { get; private set; }

    public string? WaiverReason { get; private set; }

    public DateTimeOffset? WaivedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsSatisfied => Status is KycRequirementStatus.Passed or KycRequirementStatus.Waived;

    internal static KycRequirement Required(string code, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new KycRequirement(code.Trim().ToLowerInvariant(), KycRequirementStatus.Required, null, null, null, null, now);
    }

    public static KycRequirement Restore(
        string code,
        KycRequirementStatus status,
        string? linkedVerificationId,
        string? waiverAuthority,
        string? waiverReason,
        DateTimeOffset? waivedAt,
        DateTimeOffset updatedAt) =>
        new(code, status, linkedVerificationId, waiverAuthority, waiverReason, waivedAt, updatedAt);

    /// <summary>
    /// Invalidates a satisfied requirement after a protected-data correction, so the affected verification is
    /// never treated as current. Decision: epic 03 CUS-UC-06, invariant 15.
    /// </summary>
    internal void ReopenForEvidence(DateTimeOffset now)
    {
        if (Status is KycRequirementStatus.Required or KycRequirementStatus.EvidencePending)
        {
            throw new OnboardingRuleException(
                "kyc_requirement_reopen_invalid",
                "Only a satisfied or undecided requirement can be reopened for new evidence.");
        }

        Status = KycRequirementStatus.EvidencePending;
        WaiverAuthority = null;
        WaiverReason = null;
        WaivedAt = null;
        UpdatedAt = now;
    }

    internal void BeginEvidence(DateTimeOffset now)
    {
        if (Status != KycRequirementStatus.Required)
        {
            throw new OnboardingRuleException("kyc_requirement_transition_invalid", "Evidence can only be registered for a required item.");
        }

        Status = KycRequirementStatus.EvidencePending;
        UpdatedAt = now;
    }

    internal void MarkReady(DateTimeOffset now)
    {
        if (Status != KycRequirementStatus.EvidencePending)
        {
            throw new OnboardingRuleException("kyc_requirement_transition_invalid", "Only an item with pending evidence can become ready.");
        }

        Status = KycRequirementStatus.Ready;
        UpdatedAt = now;
    }

    internal void Start(string verificationId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationId);

        if (Status is not (KycRequirementStatus.Required or KycRequirementStatus.EvidencePending or KycRequirementStatus.Ready))
        {
            throw new OnboardingRuleException("kyc_requirement_transition_invalid", "Only an outstanding item can start a verification.");
        }

        Status = KycRequirementStatus.InProgress;
        LinkedVerificationId = verificationId;
        UpdatedAt = now;
    }

    /// <summary>
    /// Applies a canonical outcome. An accepted outcome is final for this requirement; a reviewer may only
    /// resolve an inconclusive or manual-review item, never overwrite a pass or a fail.
    /// </summary>
    internal void ApplyOutcome(CanonicalVerificationOutcome outcome, DateTimeOffset now)
    {
        if (Status is KycRequirementStatus.Passed or KycRequirementStatus.Failed or KycRequirementStatus.Waived)
        {
            throw new OnboardingRuleException("kyc_requirement_transition_invalid", "A satisfied or failed requirement cannot be re-resolved.");
        }

        if (Status is not (KycRequirementStatus.InProgress or KycRequirementStatus.Inconclusive or KycRequirementStatus.ManualReview))
        {
            throw new OnboardingRuleException("kyc_requirement_transition_invalid", "Only an in-progress or review item can receive an outcome.");
        }

        Status = outcome switch
        {
            CanonicalVerificationOutcome.Passed => KycRequirementStatus.Passed,
            CanonicalVerificationOutcome.Failed => KycRequirementStatus.Failed,
            CanonicalVerificationOutcome.Inconclusive => KycRequirementStatus.Inconclusive,
            CanonicalVerificationOutcome.ManualReview => KycRequirementStatus.ManualReview,
            _ => throw new OnboardingRuleException("kyc_outcome_invalid", "An unknown canonical outcome cannot be applied."),
        };
        UpdatedAt = now;
    }

    internal void MarkManualReview(DateTimeOffset now)
    {
        if (Status is not (KycRequirementStatus.InProgress or KycRequirementStatus.Inconclusive))
        {
            throw new OnboardingRuleException("kyc_requirement_transition_invalid", "Only an in-progress or inconclusive item can be sent to manual review.");
        }

        Status = KycRequirementStatus.ManualReview;
        UpdatedAt = now;
    }

    internal void Waive(string authority, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);
        string trimmedReason = (reason ?? string.Empty).Trim();

        if (trimmedReason.Length == 0 || trimmedReason.Length > MaximumReasonLength)
        {
            throw new OnboardingRuleException("kyc_waiver_reason_required", "A waiver requires a recorded reason.");
        }

        if (Status is KycRequirementStatus.Passed or KycRequirementStatus.Waived)
        {
            throw new OnboardingRuleException("kyc_requirement_transition_invalid", "A satisfied requirement cannot be waived.");
        }

        Status = KycRequirementStatus.Waived;
        WaiverAuthority = authority;
        WaiverReason = trimmedReason;
        WaivedAt = now;
        UpdatedAt = now;
    }
}

/// <summary>
/// The KYC case for one application. It is opened from the requirement snapshot of the submitted revision,
/// so the checks it asks for are the ones the application was actually judged against, and it completes
/// only when every requirement is passed or explicitly waived. Decision: P1-E03-D06, invariant 3.
/// </summary>
public sealed class KycCase
{
    public const int MaximumRequirements = 200;

    private readonly List<KycRequirement> _requirements;

    private KycCase(
        InstitutionId institutionId,
        string caseId,
        string applicationId,
        string partyId,
        string requirementSnapshotId,
        KycCaseStatus status,
        IReadOnlyList<KycRequirement> requirements,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? completedAt)
    {
        InstitutionId = institutionId;
        CaseId = caseId;
        ApplicationId = applicationId;
        PartyId = partyId;
        RequirementSnapshotId = requirementSnapshotId;
        Status = status;
        _requirements = [.. requirements];
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        CompletedAt = completedAt;
    }

    public InstitutionId InstitutionId { get; }

    public string CaseId { get; }

    public string ApplicationId { get; }

    public string PartyId { get; }

    public string RequirementSnapshotId { get; }

    public KycCaseStatus Status { get; private set; }

    public IReadOnlyList<KycRequirement> Requirements => _requirements;

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public bool IsComplete => _requirements.All(requirement => requirement.IsSatisfied);

    public static KycCase Open(
        InstitutionId institutionId,
        string applicationId,
        string partyId,
        string requirementSnapshotId,
        IReadOnlyList<string> requirementCodes,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requirementSnapshotId);
        ArgumentNullException.ThrowIfNull(requirementCodes);

        if (requirementCodes.Count > MaximumRequirements)
        {
            throw new OnboardingRuleException("kyc_case_invalid", "A case may not exceed the bounded requirement count.");
        }

        List<KycRequirement> requirements = [];
        foreach (string code in requirementCodes)
        {
            string normalised = (code ?? string.Empty).Trim().ToLowerInvariant();
            if (normalised.Length == 0)
            {
                throw new OnboardingRuleException("kyc_case_invalid", "A requirement code is required.");
            }

            if (requirements.Any(requirement => string.Equals(requirement.Code, normalised, StringComparison.Ordinal)))
            {
                throw new OnboardingRuleException("kyc_case_invalid", "A requirement code may appear only once in a case.");
            }

            requirements.Add(KycRequirement.Required(normalised, now));
        }

        KycCase opened = new(
            institutionId,
            Guid.NewGuid().ToString("N"),
            applicationId,
            partyId,
            requirementSnapshotId,
            KycCaseStatus.Open,
            requirements,
            now,
            now,
            completedAt: null);

        opened.EvaluateCompletion(now);
        return opened;
    }

    public static KycCase Restore(
        InstitutionId institutionId,
        string caseId,
        string applicationId,
        string partyId,
        string requirementSnapshotId,
        KycCaseStatus status,
        IReadOnlyList<KycRequirement> requirements,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? completedAt) =>
        new(
            institutionId,
            caseId,
            applicationId,
            partyId,
            requirementSnapshotId,
            status,
            requirements,
            createdAt,
            updatedAt,
            completedAt);

    public KycRequirement Require(string requirementCode) =>
        _requirements.SingleOrDefault(requirement => string.Equals(requirement.Code, requirementCode, StringComparison.Ordinal))
        ?? throw new OnboardingRuleException("kyc_requirement_not_found", $"Requirement '{requirementCode}' is not part of this case.");

    /// <summary>Reopens one requirement so a correction invalidates the evidence that satisfied it.</summary>
    public void ReopenForEvidence(string requirementCode, DateTimeOffset now) =>
        Require(requirementCode).ReopenForEvidence(now);

    public void RegisterEvidence(string requirementCode, DateTimeOffset now)
    {
        EnsureOpen();
        Require(requirementCode).BeginEvidence(now);
        Touch(now);
    }

    public void MarkReady(string requirementCode, DateTimeOffset now)
    {
        EnsureOpen();
        Require(requirementCode).MarkReady(now);
        Touch(now);
    }

    public void StartVerification(string requirementCode, string verificationId, DateTimeOffset now)
    {
        EnsureOpen();
        Require(requirementCode).Start(verificationId, now);
        Touch(now);
    }

    public void ApplyOutcome(string requirementCode, CanonicalVerificationOutcome outcome, DateTimeOffset now)
    {
        EnsureOpen();
        Require(requirementCode).ApplyOutcome(outcome, now);
        Touch(now);
        EvaluateCompletion(now);
    }

    public void MarkManualReview(string requirementCode, DateTimeOffset now)
    {
        EnsureOpen();
        Require(requirementCode).MarkManualReview(now);
        Touch(now);
        EvaluateCompletion(now);
    }

    public void Waive(string requirementCode, string authority, string reason, DateTimeOffset now)
    {
        EnsureOpen();
        Require(requirementCode).Waive(authority, reason, now);
        Touch(now);
        EvaluateCompletion(now);
    }

    private void EnsureOpen()
    {
        if (Status == KycCaseStatus.Completed)
        {
            throw new OnboardingRuleException("kyc_case_completed", "A completed case cannot be changed; a new revision or case is required.");
        }
    }

    private void Touch(DateTimeOffset now) => UpdatedAt = now;

    private void EvaluateCompletion(DateTimeOffset now)
    {
        if (IsComplete)
        {
            Status = KycCaseStatus.Completed;
            CompletedAt ??= now;
        }
    }
}
