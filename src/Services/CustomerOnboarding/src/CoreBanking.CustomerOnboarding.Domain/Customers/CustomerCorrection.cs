using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CustomerOnboarding.Domain.Customers;

/// <summary>
/// One append-only protected-data correction. It records the reason, the prior and the new protected-value
/// references (never the values), and whether an affected verification has to be repeated. Decision: epic 03
/// CUS-UC-06, invariant 15.
/// </summary>
public sealed class CustomerCorrection
{
    private const int MaximumReasonLength = 500;
    private const int MaximumReferenceLength = 256;

    private CustomerCorrection(
        InstitutionId institutionId,
        string correctionId,
        string customerId,
        string fieldCode,
        string priorValueReference,
        string newValueReference,
        string reason,
        string? evidenceReference,
        bool requiresReverification,
        string? requirementCode,
        string? approvalReference,
        string recordedBy,
        DateTimeOffset recordedAt)
    {
        InstitutionId = institutionId;
        CorrectionId = correctionId;
        CustomerId = customerId;
        FieldCode = fieldCode;
        PriorValueReference = priorValueReference;
        NewValueReference = newValueReference;
        Reason = reason;
        EvidenceReference = evidenceReference;
        RequiresReverification = requiresReverification;
        RequirementCode = requirementCode;
        ApprovalReference = approvalReference;
        RecordedBy = recordedBy;
        RecordedAt = recordedAt;
    }

    public InstitutionId InstitutionId { get; }

    public string CorrectionId { get; }

    public string CustomerId { get; }

    /// <summary>The affected field as an approved classification code, never a free-text path.</summary>
    public string FieldCode { get; }

    /// <summary>Opaque reference to the superseded protected value. The value itself is not stored here.</summary>
    public string PriorValueReference { get; }

    /// <summary>Opaque reference to the new protected value.</summary>
    public string NewValueReference { get; }

    public string Reason { get; }

    public string? EvidenceReference { get; }

    public bool RequiresReverification { get; }

    /// <summary>The KYC requirement that must be evidenced again, when the correction invalidates one.</summary>
    public string? RequirementCode { get; }

    /// <summary>The Workflow approval that authorised a verified-identifier correction.</summary>
    public string? ApprovalReference { get; }

    public string RecordedBy { get; }

    public DateTimeOffset RecordedAt { get; }

    public static CustomerCorrection Record(
        InstitutionId institutionId,
        string customerId,
        string fieldCode,
        string priorValueReference,
        string newValueReference,
        string reason,
        string? evidenceReference,
        bool isVerifiedIdentifier,
        string? requirementCode,
        string? approvalReference,
        string recordedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(institutionId);

        string field = StableCode(fieldCode, "fieldCode", 64, "customer_correction_field_invalid");
        string prior = StableCode(priorValueReference, "priorValueReference", MaximumReferenceLength, "customer_correction_reference_invalid");
        string updated = StableCode(newValueReference, "newValueReference", MaximumReferenceLength, "customer_correction_reference_invalid");

        if (string.Equals(prior, updated, StringComparison.Ordinal))
        {
            throw new OnboardingRuleException(
                "customer_correction_unchanged",
                "A correction must change the protected value it cites.");
        }

        string trimmedReason = (reason ?? string.Empty).Trim();
        if (trimmedReason.Length < 3 || trimmedReason.Length > MaximumReasonLength)
        {
            throw new OnboardingRuleException(
                "customer_correction_reason_required",
                "A correction requires a recorded reason.");
        }

        string? requirement = string.IsNullOrWhiteSpace(requirementCode)
            ? null
            : StableCode(requirementCode, "requirementCode", 64, "customer_correction_requirement_invalid");

        string? approval = string.IsNullOrWhiteSpace(approvalReference) ? null : approvalReference.Trim();

        // A customer identifier or other verified attribute is never corrected without an approval, and it
        // always invalidates the verification that previously satisfied the requirement.
        if (isVerifiedIdentifier && approval is null)
        {
            throw new OnboardingRuleException(
                "customer_correction_approval_required",
                "Correcting a verified attribute requires an approval reference.");
        }

        bool reverification = isVerifiedIdentifier || requirement is not null;
        if (reverification && requirement is null)
        {
            throw new OnboardingRuleException(
                "customer_correction_requirement_required",
                "A correction that invalidates a verification must name the requirement to re-evidence.");
        }

        return new CustomerCorrection(
            institutionId,
            Guid.NewGuid().ToString("N"),
            StableCode(customerId, "customerId", 64, "customer_correction_customer_invalid"),
            field,
            prior,
            updated,
            trimmedReason,
            string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim(),
            reverification,
            requirement,
            approval,
            StableCode(recordedBy, "recordedBy", 128, "customer_correction_actor_invalid"),
            now);
    }

    public static CustomerCorrection Restore(
        InstitutionId institutionId,
        string correctionId,
        string customerId,
        string fieldCode,
        string priorValueReference,
        string newValueReference,
        string reason,
        string? evidenceReference,
        bool requiresReverification,
        string? requirementCode,
        string? approvalReference,
        string recordedBy,
        DateTimeOffset recordedAt) =>
        new(
            institutionId,
            correctionId,
            customerId,
            fieldCode,
            priorValueReference,
            newValueReference,
            reason,
            evidenceReference,
            requiresReverification,
            requirementCode,
            approvalReference,
            recordedBy,
            recordedAt);

    private static string StableCode(string? value, string field, int maxLength, string errorCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OnboardingRuleException(errorCode, $"A correction requires {field}.");
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength || trimmed.Any(char.IsWhiteSpace))
        {
            throw new OnboardingRuleException(errorCode, $"A correction {field} must be a stable code.");
        }

        return trimmed;
    }
}
