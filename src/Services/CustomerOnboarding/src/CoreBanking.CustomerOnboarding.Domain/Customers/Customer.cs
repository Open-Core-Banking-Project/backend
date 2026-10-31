using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CustomerOnboarding.Domain.Customers;

/// <summary>Customer lifecycle from the epic. Terminal states are never reversed by code. Decision: P1-E03-D10.</summary>
public enum CustomerStatus
{
    Pending,
    Active,
    Restricted,
    Dormant,
    Deceased,
    Closed,
    Rejected,
}

/// <summary>A classification version is usable only after Workflow approval. Decision: P1-E03-D09.</summary>
public enum ClassificationStatus
{
    Proposed,
    Approved,
    Superseded,
}

/// <summary>Stable storage and wire codes for customer enums.</summary>
public static class CustomerCodes
{
    public static string ToCode(CustomerStatus status) => status switch
    {
        CustomerStatus.Pending => "pending",
        CustomerStatus.Active => "active",
        CustomerStatus.Restricted => "restricted",
        CustomerStatus.Dormant => "dormant",
        CustomerStatus.Deceased => "deceased",
        CustomerStatus.Closed => "closed",
        CustomerStatus.Rejected => "rejected",
        _ => throw new OnboardingRuleException("customer_status_invalid", $"Customer status '{status}' has no code."),
    };

    public static CustomerStatus FromStatusCode(string code) => code switch
    {
        "pending" => CustomerStatus.Pending,
        "active" => CustomerStatus.Active,
        "restricted" => CustomerStatus.Restricted,
        "dormant" => CustomerStatus.Dormant,
        "deceased" => CustomerStatus.Deceased,
        "closed" => CustomerStatus.Closed,
        "rejected" => CustomerStatus.Rejected,
        _ => throw new OnboardingRuleException("customer_status_invalid", $"Customer status code '{code}' is not known."),
    };

    public static string ToCode(ClassificationStatus status) => status switch
    {
        ClassificationStatus.Proposed => "proposed",
        ClassificationStatus.Approved => "approved",
        ClassificationStatus.Superseded => "superseded",
        _ => throw new OnboardingRuleException("customer_classification_status_invalid", $"Classification status '{status}' has no code."),
    };

    public static ClassificationStatus FromClassificationStatusCode(string code) => code switch
    {
        "proposed" => ClassificationStatus.Proposed,
        "approved" => ClassificationStatus.Approved,
        "superseded" => ClassificationStatus.Superseded,
        _ => throw new OnboardingRuleException("customer_classification_status_invalid", $"Classification status code '{code}' is not known."),
    };
}

/// <summary>One recorded status transition. History is append-only. Decision: P1-E03-D10.</summary>
public sealed record CustomerStatusChange(
    CustomerStatus From,
    CustomerStatus To,
    string Reason,
    string ActorId,
    string? ApprovalReference,
    DateTimeOffset OccurredAt);

/// <summary>
/// A versioned KYC tier, segment, risk class and EDD flag. The values are approved configuration and Risk
/// references; only an approved version can activate a customer. Decision: P1-E03-D09.
/// </summary>
public sealed class CustomerClassification
{
    private const int MaximumCodeLength = 64;

    private CustomerClassification(
        int version,
        string tier,
        string segment,
        string riskClass,
        bool requiresEnhancedDueDiligence,
        ClassificationStatus status,
        string? workflowDecisionReference,
        DateTimeOffset? approvedAt,
        DateTimeOffset proposedAt,
        DateTimeOffset updatedAt,
        string? approvalRequestId = null,
        string? approvalContentHash = null,
        DateTimeOffset? submittedForApprovalAt = null)
    {
        Version = version;
        Tier = tier;
        Segment = segment;
        RiskClass = riskClass;
        RequiresEnhancedDueDiligence = requiresEnhancedDueDiligence;
        Status = status;
        WorkflowDecisionReference = workflowDecisionReference;
        ApprovedAt = approvedAt;
        ProposedAt = proposedAt;
        UpdatedAt = updatedAt;
        ApprovalRequestId = approvalRequestId;
        ApprovalContentHash = approvalContentHash;
        SubmittedForApprovalAt = submittedForApprovalAt;
    }

    public int Version { get; }

    public string Tier { get; }

    public string Segment { get; }

    public string RiskClass { get; }

    public bool RequiresEnhancedDueDiligence { get; }

    public ClassificationStatus Status { get; private set; }

    public string? WorkflowDecisionReference { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset ProposedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsUsableForActivation => Status == ClassificationStatus.Approved;

    /// <summary>The Workflow request that is awaiting a decision, when the version was submitted for approval.</summary>
    public string? ApprovalRequestId { get; private set; }

    /// <summary>The content hash the approver decides on, so a stale approval cannot be applied.</summary>
    public string? ApprovalContentHash { get; private set; }

    public DateTimeOffset? SubmittedForApprovalAt { get; private set; }

    public static CustomerClassification Propose(
        int version,
        string tier,
        string segment,
        string riskClass,
        bool requiresEnhancedDueDiligence,
        DateTimeOffset now)
    {
        if (version < 1)
        {
            throw new OnboardingRuleException("customer_classification_invalid", "A classification version starts at one.");
        }

        return new CustomerClassification(
            version,
            ValidateCode(tier, "tier"),
            ValidateCode(segment, "segment"),
            ValidateCode(riskClass, "riskClass"),
            requiresEnhancedDueDiligence,
            ClassificationStatus.Proposed,
            workflowDecisionReference: null,
            approvedAt: null,
            now,
            now);
    }

    public static CustomerClassification Restore(
        int version,
        string tier,
        string segment,
        string riskClass,
        bool requiresEnhancedDueDiligence,
        ClassificationStatus status,
        string? workflowDecisionReference,
        DateTimeOffset? approvedAt,
        DateTimeOffset proposedAt,
        DateTimeOffset updatedAt,
        string? approvalRequestId = null,
        string? approvalContentHash = null,
        DateTimeOffset? submittedForApprovalAt = null) =>
        new(
            version,
            tier,
            segment,
            riskClass,
            requiresEnhancedDueDiligence,
            status,
            workflowDecisionReference,
            approvedAt,
            proposedAt,
            updatedAt,
            approvalRequestId,
            approvalContentHash,
            submittedForApprovalAt);

    /// <summary>
    /// A deterministic digest of the exact version an approver is deciding on. A decision that cites any other
    /// hash is refused rather than applied to a version it never saw. Decision: P1-E03-D09.
    /// </summary>
    public static string ComputeContentHash(
        string customerId,
        int version,
        string tier,
        string segment,
        string riskClass,
        bool requiresEnhancedDueDiligence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tier);
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentException.ThrowIfNullOrWhiteSpace(riskClass);

        string material = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"classification|{customerId.Trim().ToLowerInvariant()}|{version}|{tier.Trim().ToLowerInvariant()}|{segment.Trim().ToLowerInvariant()}|{riskClass.Trim().ToLowerInvariant()}|{(requiresEnhancedDueDiligence ? 1 : 0)}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    /// <summary>Records that this exact version was submitted to Workflow for a decision.</summary>
    public void SubmitForApproval(string approvalRequestId, string contentHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalRequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        if (Status != ClassificationStatus.Proposed)
        {
            throw new OnboardingRuleException(
                "customer_classification_transition_invalid",
                "Only a proposed classification can be submitted for approval.");
        }

        if (contentHash.Length != 64 || !contentHash.All(Uri.IsHexDigit))
        {
            throw new OnboardingRuleException(
                "customer_classification_hash_invalid",
                "A classification content hash must be a SHA-256 hex digest.");
        }

        if (ApprovalContentHash is not null
            && !string.Equals(ApprovalContentHash, contentHash, StringComparison.Ordinal))
        {
            throw new OnboardingRuleException(
                "customer_classification_approval_in_flight",
                "This version already has an approval request for different content.");
        }

        ApprovalRequestId = approvalRequestId.Trim();
        ApprovalContentHash = contentHash.ToLowerInvariant();
        SubmittedForApprovalAt = now;
        UpdatedAt = now;
    }

    /// <summary>Approval must cite the exact Workflow decision; a proposal never becomes usable by itself.</summary>
    public void Approve(string workflowDecisionReference, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDecisionReference);

        if (Status != ClassificationStatus.Proposed)
        {
            throw new OnboardingRuleException("customer_classification_transition_invalid", "Only a proposed classification can be approved.");
        }

        Status = ClassificationStatus.Approved;
        WorkflowDecisionReference = workflowDecisionReference.Trim();
        ApprovedAt = now;
        UpdatedAt = now;
    }

    public void Supersede(DateTimeOffset now)
    {
        if (Status != ClassificationStatus.Approved)
        {
            throw new OnboardingRuleException("customer_classification_transition_invalid", "Only an approved classification can be superseded.");
        }

        Status = ClassificationStatus.Superseded;
        UpdatedAt = now;
    }

    private static string ValidateCode(string value, string field)
    {
        string trimmed = (value ?? string.Empty).Trim();
        bool valid = trimmed.Length is > 0 and <= MaximumCodeLength
            && char.IsAsciiLetter(trimmed[0])
            && trimmed.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

        return valid
            ? trimmed.ToLowerInvariant()
            : throw new OnboardingRuleException(
                "customer_classification_invalid",
                $"A classification {field} must be a stable code supplied by approved configuration.");
    }
}

/// <summary>
/// The institution-specific banking relationship. Lifecycle transitions follow the epic exactly, require a
/// reason, record approval references where policy demands one, and never touch an account: downstream
/// services consume the published events. Decision: P1-E03-D09, P1-E03-D10.
/// </summary>
public sealed class Customer
{
    private const int MinimumReasonLength = 3;
    private const int MaximumReasonLength = 500;

    private static readonly Dictionary<CustomerStatus, CustomerStatus[]> AllowedTransitions =
        new Dictionary<CustomerStatus, CustomerStatus[]>
        {
            [CustomerStatus.Pending] = [CustomerStatus.Active, CustomerStatus.Rejected],
            [CustomerStatus.Active] = [CustomerStatus.Restricted, CustomerStatus.Dormant, CustomerStatus.Deceased, CustomerStatus.Closed],
            [CustomerStatus.Restricted] = [CustomerStatus.Active, CustomerStatus.Closed],
            [CustomerStatus.Dormant] = [CustomerStatus.Active, CustomerStatus.Closed],
            [CustomerStatus.Deceased] = [],
            [CustomerStatus.Closed] = [],
            [CustomerStatus.Rejected] = [],
        };

    private static readonly HashSet<CustomerStatus> ApprovalRequiredTargets =
        [CustomerStatus.Restricted, CustomerStatus.Deceased, CustomerStatus.Closed];

    private readonly List<CustomerClassification> _classifications;
    private readonly List<CustomerStatusChange> _statusHistory;

    private Customer(
        InstitutionId institutionId,
        string customerId,
        string partyId,
        string customerNumber,
        string? branchId,
        CustomerStatus status,
        IReadOnlyList<CustomerClassification> classifications,
        IReadOnlyList<CustomerStatusChange> statusHistory,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        long concurrencyVersion)
    {
        InstitutionId = institutionId;
        CustomerId = customerId;
        PartyId = partyId;
        CustomerNumber = customerNumber;
        BranchId = branchId;
        Status = status;
        _classifications = [.. classifications];
        _statusHistory = [.. statusHistory];
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        ConcurrencyVersion = concurrencyVersion;
    }

    public InstitutionId InstitutionId { get; }

    public string CustomerId { get; }

    public string PartyId { get; }

    /// <summary>Institution-unique relationship number from the institution sequence. Presentation formatting is Operations data.</summary>
    public string CustomerNumber { get; }

    public string? BranchId { get; }

    public CustomerStatus Status { get; private set; }

    public IReadOnlyList<CustomerClassification> Classifications => _classifications;

    public IReadOnlyList<CustomerStatusChange> StatusHistory => _statusHistory;

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long ConcurrencyVersion { get; private set; }

    /// <summary>The approved classification version, or null when none is approved yet.</summary>
    public CustomerClassification? EffectiveClassification =>
        _classifications.LastOrDefault(classification => classification.Status == ClassificationStatus.Approved);

    public static Customer Register(
        InstitutionId institutionId,
        string partyId,
        string customerNumber,
        string? branchId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerNumber);

        return new Customer(
            institutionId,
            Guid.NewGuid().ToString("N"),
            partyId.Trim(),
            customerNumber.Trim(),
            string.IsNullOrWhiteSpace(branchId) ? null : branchId.Trim(),
            CustomerStatus.Pending,
            classifications: [],
            statusHistory: [],
            now,
            now,
            concurrencyVersion: 1L);
    }

    public static Customer Restore(
        InstitutionId institutionId,
        string customerId,
        string partyId,
        string customerNumber,
        string? branchId,
        CustomerStatus status,
        IReadOnlyList<CustomerClassification> classifications,
        IReadOnlyList<CustomerStatusChange> statusHistory,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        long concurrencyVersion) =>
        new(
            institutionId,
            customerId,
            partyId,
            customerNumber,
            branchId,
            status,
            classifications,
            statusHistory,
            createdAt,
            updatedAt,
            concurrencyVersion);

    public CustomerClassification ProposeClassification(
        string tier,
        string segment,
        string riskClass,
        bool requiresEnhancedDueDiligence,
        DateTimeOffset now)
    {
        if (Status is CustomerStatus.Deceased or CustomerStatus.Closed)
        {
            throw new OnboardingRuleException("customer_status_terminal", "A deceased or closed customer cannot receive a new classification.");
        }

        int nextVersion = _classifications.Count == 0 ? 1 : _classifications.Max(classification => classification.Version) + 1;
        CustomerClassification classification = CustomerClassification.Propose(
            nextVersion,
            tier,
            segment,
            riskClass,
            requiresEnhancedDueDiligence,
            now);

        _classifications.Add(classification);
        Touch(now);
        return classification;
    }

    public CustomerClassification ApproveClassification(int version, string workflowDecisionReference, DateTimeOffset now)
    {
        CustomerClassification classification = _classifications.SingleOrDefault(candidate => candidate.Version == version)
            ?? throw new OnboardingRuleException("customer_classification_not_found", $"Classification version '{version}' does not exist.");

        classification.Approve(workflowDecisionReference, now);

        foreach (CustomerClassification earlier in _classifications.Where(candidate =>
            candidate.Version != version && candidate.Status == ClassificationStatus.Approved))
        {
            earlier.Supersede(now);
        }

        Touch(now);
        return classification;
    }

    /// <summary>
    /// Activation needs an approved classification. KYC completion is checked by the caller, because the
    /// case is a separate aggregate with its own evidence.
    /// </summary>
    public void Activate(int classificationVersion, DateTimeOffset now)
    {
        if (Status != CustomerStatus.Pending)
        {
            throw new OnboardingRuleException("customer_transition_forbidden", "Only a pending customer can be activated.");
        }

        CustomerClassification? classification = _classifications.SingleOrDefault(candidate =>
            candidate.Version == classificationVersion);
        if (classification is null || !classification.IsUsableForActivation)
        {
            throw new OnboardingRuleException(
                "customer_classification_required",
                "Activation requires an approved classification version.");
        }

        ApplyTransition(
            CustomerStatus.Active,
            "activation",
            "system",
            workflowApprovalReference: classification.WorkflowDecisionReference,
            now);
    }

    public void ChangeStatus(
        CustomerStatus target,
        string reason,
        string actorId,
        string? approvalReference,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        string trimmedReason = (reason ?? string.Empty).Trim();
        if (trimmedReason.Length < MinimumReasonLength || trimmedReason.Length > MaximumReasonLength)
        {
            throw new OnboardingRuleException("customer_status_reason_required", "A status change requires a recorded reason.");
        }

        ApplyTransition(target, trimmedReason, actorId.Trim(), approvalReference, now);
    }

    private void ApplyTransition(
        CustomerStatus target,
        string reason,
        string actorId,
        string? workflowApprovalReference,
        DateTimeOffset now)
    {
        if (!AllowedTransitions[Status].Contains(target))
        {
            throw new OnboardingRuleException(
                "customer_transition_forbidden",
                $"The transition from '{CustomerCodes.ToCode(Status)}' to '{CustomerCodes.ToCode(target)}' is not allowed by policy.");
        }

        if (ApprovalRequiredTargets.Contains(target) && string.IsNullOrWhiteSpace(workflowApprovalReference))
        {
            throw new OnboardingRuleException(
                "customer_approval_required",
                $"The transition to '{CustomerCodes.ToCode(target)}' requires an approval reference.");
        }

        CustomerStatusChange change = new(
            Status,
            target,
            reason,
            actorId,
            string.IsNullOrWhiteSpace(workflowApprovalReference) ? null : workflowApprovalReference.Trim(),
            now);

        _statusHistory.Add(change);
        Status = target;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        ConcurrencyVersion++;
    }
}
