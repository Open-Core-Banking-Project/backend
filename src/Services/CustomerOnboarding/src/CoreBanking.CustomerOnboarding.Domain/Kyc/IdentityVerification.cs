using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CustomerOnboarding.Domain.Kyc;

/// <summary>Logical verification lifecycle from the epic: created, submitted, pending, resolved or uncertain.</summary>
public enum VerificationStatus
{
    Created,
    Submitted,
    Pending,
    Succeeded,
    Failed,
    Uncertain,
    ManualReview,
    Cancelled,
}

/// <summary>One provider attempt. An attempt is evidence: a new call is a new attempt, never an edit.</summary>
public enum VerificationAttemptStatus
{
    Submitted,
    Pending,
    Succeeded,
    Failed,
    Uncertain,
    CancelledBeforeSubmission,
}

/// <summary>
/// One immutable provider attempt of a logical verification. Decision: P1-E03-D06, invariant 8.
/// </summary>
public sealed class VerificationAttempt
{
    private VerificationAttempt(
        string attemptId,
        string? providerReference,
        string? providerRouteId,
        VerificationAttemptStatus status,
        DateTimeOffset submittedAt,
        DateTimeOffset? resolvedAt)
    {
        AttemptId = attemptId;
        ProviderReference = providerReference;
        ProviderRouteId = providerRouteId;
        Status = status;
        SubmittedAt = submittedAt;
        ResolvedAt = resolvedAt;
    }

    public string AttemptId { get; }

    public string? ProviderReference { get; }

    /// <summary>The approved provider route that carried this attempt. Decision: P1-E03-D07.</summary>
    public string? ProviderRouteId { get; }

    public VerificationAttemptStatus Status { get; private set; }

    public DateTimeOffset SubmittedAt { get; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public bool IsTerminal => Status is VerificationAttemptStatus.Succeeded
        or VerificationAttemptStatus.Failed
        or VerificationAttemptStatus.CancelledBeforeSubmission;

    internal static VerificationAttempt Submit(
        string attemptId,
        string? providerReference,
        string? providerRouteId,
        DateTimeOffset now) =>
        new(attemptId, providerReference, providerRouteId, VerificationAttemptStatus.Submitted, now, null);

    public static VerificationAttempt Restore(
        string attemptId,
        string? providerReference,
        string? providerRouteId,
        VerificationAttemptStatus status,
        DateTimeOffset submittedAt,
        DateTimeOffset? resolvedAt) =>
        new(attemptId, providerReference, providerRouteId, status, submittedAt, resolvedAt);

    internal void Resolve(VerificationAttemptStatus status, DateTimeOffset now)
    {
        Status = status;
        if (status != VerificationAttemptStatus.Pending)
        {
            ResolvedAt = now;
        }
    }
}

/// <summary>
/// The reference to raw provider evidence and its hash. Decision: P1-E03-D08 - this service stores the
/// reference only, never the payload.
/// </summary>
public sealed record VerificationEvidence(string EvidenceReference, string EvidenceHash);

/// <summary>
/// A canonical verification for one requirement of one application. Provider results reach it only through
/// a versioned mapping; an uncertain or unmapped answer is never a pass, and two accepted outcomes for one
/// verification are impossible. Decision: P1-E03-D06, invariants 8 and 10.
/// </summary>
public sealed class IdentityVerification
{
    private readonly List<VerificationAttempt> _attempts;

    private IdentityVerification(
        InstitutionId institutionId,
        string verificationId,
        string applicationId,
        string partyId,
        string requirementCode,
        string checkCode,
        string requirementSnapshotId,
        VerificationStatus status,
        CanonicalVerificationOutcome? outcome,
        string? providerMappingVersion,
        string? evidenceReference,
        string? evidenceHash,
        IReadOnlyList<VerificationAttempt> attempts,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        InstitutionId = institutionId;
        VerificationId = verificationId;
        ApplicationId = applicationId;
        PartyId = partyId;
        RequirementCode = requirementCode;
        CheckCode = checkCode;
        RequirementSnapshotId = requirementSnapshotId;
        Status = status;
        Outcome = outcome;
        ProviderMappingVersion = providerMappingVersion;
        EvidenceReference = evidenceReference;
        EvidenceHash = evidenceHash;
        _attempts = [.. attempts];
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public InstitutionId InstitutionId { get; }

    public string VerificationId { get; }

    public string ApplicationId { get; }

    public string PartyId { get; }

    public string RequirementCode { get; }

    public string CheckCode { get; }

    public string RequirementSnapshotId { get; }

    public VerificationStatus Status { get; private set; }

    public CanonicalVerificationOutcome? Outcome { get; private set; }

    public string? ProviderMappingVersion { get; private set; }

    /// <summary>Opaque reference to the raw provider evidence; the payload is never stored here.</summary>
    public string? EvidenceReference { get; private set; }

    /// <summary>SHA-256 hex digest of the raw provider evidence that produced the accepted outcome.</summary>
    public string? EvidenceHash { get; private set; }

    public IReadOnlyList<VerificationAttempt> Attempts => _attempts;

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsTerminal => Status is VerificationStatus.Succeeded or VerificationStatus.Failed or VerificationStatus.Cancelled;

    public static IdentityVerification Request(
        InstitutionId institutionId,
        string applicationId,
        string partyId,
        string requirementCode,
        string checkCode,
        string requirementSnapshotId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requirementCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(requirementSnapshotId);

        return new IdentityVerification(
            institutionId,
            Guid.NewGuid().ToString("N"),
            applicationId,
            partyId,
            requirementCode.Trim().ToLowerInvariant(),
            checkCode.Trim().ToLowerInvariant(),
            requirementSnapshotId,
            VerificationStatus.Created,
            outcome: null,
            providerMappingVersion: null,
            evidenceReference: null,
            evidenceHash: null,
            attempts: [],
            now,
            now);
    }

    public static IdentityVerification Restore(
        InstitutionId institutionId,
        string verificationId,
        string applicationId,
        string partyId,
        string requirementCode,
        string checkCode,
        string requirementSnapshotId,
        VerificationStatus status,
        CanonicalVerificationOutcome? outcome,
        string? providerMappingVersion,
        string? evidenceReference,
        string? evidenceHash,
        IReadOnlyList<VerificationAttempt> attempts,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new(
            institutionId,
            verificationId,
            applicationId,
            partyId,
            requirementCode,
            checkCode,
            requirementSnapshotId,
            status,
            outcome,
            providerMappingVersion,
            evidenceReference,
            evidenceHash,
            attempts,
            createdAt,
            updatedAt);

    /// <summary>Records a new provider attempt without naming a route; the route is bound when one is approved.</summary>
    public VerificationAttempt RecordAttempt(string attemptId, string? providerReference, DateTimeOffset now) =>
        RecordAttempt(attemptId, providerReference, providerRouteId: null, now);

    /// <summary>Records a new provider attempt. A retry is a new attempt; the previous one stays untouched.</summary>
    public VerificationAttempt RecordAttempt(
        string attemptId,
        string? providerReference,
        string? providerRouteId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);

        if (IsTerminal)
        {
            throw new OnboardingRuleException("kyc_verification_closed", "A resolved verification cannot accept another attempt.");
        }

        if (_attempts.Any(attempt => string.Equals(attempt.AttemptId, attemptId, StringComparison.Ordinal)))
        {
            throw new OnboardingRuleException("kyc_attempt_duplicate", "An attempt identifier may be used only once.");
        }

        VerificationAttempt attempt = VerificationAttempt.Submit(attemptId, providerReference, providerRouteId, now);
        _attempts.Add(attempt);
        Status = VerificationStatus.Submitted;
        UpdatedAt = now;
        return attempt;
    }

    /// <summary>Applies an attempt result that carries no evidence, such as an uncertain or failed attempt.</summary>
    public void ApplyAttemptResult(
        string attemptId,
        VerificationAttemptStatus attemptStatus,
        CanonicalVerificationOutcome? outcome,
        string? providerMappingVersion,
        DateTimeOffset now) =>
        ApplyAttemptResult(attemptId, attemptStatus, outcome, providerMappingVersion, evidence: null, now);

    /// <summary>
    /// Applies one attempt result. A succeeded attempt must name the canonical outcome and the mapping
    /// version that produced it; an unmapped or missing answer is inconclusive, never a pass.
    /// </summary>
    public void ApplyAttemptResult(
        string attemptId,
        VerificationAttemptStatus attemptStatus,
        CanonicalVerificationOutcome? outcome,
        string? providerMappingVersion,
        VerificationEvidence? evidence,
        DateTimeOffset now)
    {
        if (IsTerminal)
        {
            throw new OnboardingRuleException("kyc_verification_closed", "A resolved verification cannot be resolved again.");
        }

        VerificationAttempt attempt = _attempts.SingleOrDefault(candidate =>
            string.Equals(candidate.AttemptId, attemptId, StringComparison.Ordinal))
            ?? throw new OnboardingRuleException("kyc_attempt_not_found", "The attempt is not part of this verification.");

        if (attempt.IsTerminal)
        {
            throw new OnboardingRuleException("kyc_attempt_closed", "A resolved attempt cannot be resolved again.");
        }

        attempt.Resolve(attemptStatus, now);

        switch (attemptStatus)
        {
            case VerificationAttemptStatus.Submitted:
                Status = VerificationStatus.Submitted;
                break;
            case VerificationAttemptStatus.Pending:
                Status = VerificationStatus.Pending;
                break;
            case VerificationAttemptStatus.Uncertain:
                Status = VerificationStatus.Uncertain;
                break;
            case VerificationAttemptStatus.CancelledBeforeSubmission:
                Status = VerificationStatus.Created;
                break;
            case VerificationAttemptStatus.Failed:
                Status = VerificationStatus.Failed;
                Outcome = null;
                break;
            case VerificationAttemptStatus.Succeeded:
                if (outcome is null)
                {
                    throw new OnboardingRuleException(
                        "kyc_verification_outcome_required",
                        "A succeeded attempt requires a canonical outcome from the approved mapping.");
                }

                if (string.IsNullOrWhiteSpace(providerMappingVersion))
                {
                    throw new OnboardingRuleException(
                        "kyc_mapping_version_required",
                        "A canonical outcome requires the provider mapping version that produced it.");
                }

                if (Outcome is not null)
                {
                    throw new OnboardingRuleException(
                        "kyc_verification_outcome_conflict",
                        "This verification already holds an accepted outcome and cannot accept a second one.");
                }

                if (evidence is null
                    || string.IsNullOrWhiteSpace(evidence.EvidenceReference)
                    || string.IsNullOrWhiteSpace(evidence.EvidenceHash))
                {
                    throw new OnboardingRuleException(
                        "kyc_evidence_reference_required",
                        "An accepted outcome requires the provider evidence reference and its hash.");
                }

                Outcome = outcome;
                ProviderMappingVersion = providerMappingVersion.Trim();
                EvidenceReference = evidence.EvidenceReference.Trim();
                EvidenceHash = NormaliseEvidenceHash(evidence.EvidenceHash);
                Status = outcome == CanonicalVerificationOutcome.ManualReview
                    ? VerificationStatus.ManualReview
                    : VerificationStatus.Succeeded;
                break;
            default:
                throw new OnboardingRuleException("kyc_attempt_status_invalid", "An unknown attempt status cannot be applied.");
        }

        UpdatedAt = now;
    }

    /// <summary>Sends an inconclusive or uncertain verification to a human decision.</summary>
    public void MarkManualReview(DateTimeOffset now)
    {
        if (IsTerminal)
        {
            throw new OnboardingRuleException("kyc_verification_closed", "A resolved verification cannot be sent to manual review.");
        }

        if (Status is not (VerificationStatus.Uncertain or VerificationStatus.Pending or VerificationStatus.Submitted))
        {
            throw new OnboardingRuleException("kyc_verification_transition_invalid", "Only an unresolved verification can be sent to manual review.");
        }

        Status = VerificationStatus.ManualReview;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (IsTerminal)
        {
            throw new OnboardingRuleException("kyc_verification_closed", "A resolved verification cannot be cancelled.");
        }

        Status = VerificationStatus.Cancelled;
        UpdatedAt = now;
    }

    /// <summary>Evidence hashes are lower-case SHA-256 hex so a consumer can compare them byte for byte.</summary>
    private static string NormaliseEvidenceHash(string evidenceHash)
    {
        string trimmed = evidenceHash.Trim().ToLowerInvariant();
        if (trimmed.Length != 64 || !trimmed.All(char.IsAsciiHexDigit))
        {
            throw new OnboardingRuleException(
                "kyc_evidence_hash_invalid",
                "A provider evidence hash must be a SHA-256 hex digest.");
        }

        return trimmed;
    }
}
