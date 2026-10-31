using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CustomerOnboarding.Domain.Customers;

/// <summary>Merge lifecycle from the epic. Decision: P1-E03-D11.</summary>
public enum MergeRequestStatus
{
    Proposed,
    Analysed,
    PendingApproval,
    Approved,
    Applying,
    Completed,
    ReconciliationRequired,
    Rejected,
    Withdrawn,
}

/// <summary>Stable codes for the merge lifecycle.</summary>
public static class MergeCodes
{
    public static string ToCode(MergeRequestStatus status) => status switch
    {
        MergeRequestStatus.Proposed => "proposed",
        MergeRequestStatus.Analysed => "analysed",
        MergeRequestStatus.PendingApproval => "pending_approval",
        MergeRequestStatus.Approved => "approved",
        MergeRequestStatus.Applying => "applying",
        MergeRequestStatus.Completed => "completed",
        MergeRequestStatus.ReconciliationRequired => "reconciliation_required",
        MergeRequestStatus.Rejected => "rejected",
        MergeRequestStatus.Withdrawn => "withdrawn",
        _ => throw new OnboardingRuleException("merge_status_invalid", $"Merge status '{status}' has no code."),
    };

    public static MergeRequestStatus FromStatusCode(string code) => code switch
    {
        "proposed" => MergeRequestStatus.Proposed,
        "analysed" => MergeRequestStatus.Analysed,
        "pending_approval" => MergeRequestStatus.PendingApproval,
        "approved" => MergeRequestStatus.Approved,
        "applying" => MergeRequestStatus.Applying,
        "completed" => MergeRequestStatus.Completed,
        "reconciliation_required" => MergeRequestStatus.ReconciliationRequired,
        "rejected" => MergeRequestStatus.Rejected,
        "withdrawn" => MergeRequestStatus.Withdrawn,
        _ => throw new OnboardingRuleException("merge_status_invalid", $"Merge status code '{code}' is not known."),
    };

    public static bool IsTerminal(MergeRequestStatus status) =>
        status is MergeRequestStatus.Completed or MergeRequestStatus.Rejected or MergeRequestStatus.Withdrawn;
}

/// <summary>One consumer acknowledgement of a merge. Acknowledgement is idempotent per consumer.</summary>
public sealed record MergeConsumerAcknowledgement(string Consumer, DateTimeOffset AcknowledgedAt);

/// <summary>
/// A planned survivor/source merge. The plan hash freezes survivor, source, reason and the required consumer
/// list once it is approved; applying closes the source and records an alias, and completion waits for every
/// required consumer. Decision: P1-E03-D11.
/// </summary>
public sealed class CustomerMergeRequest
{
    private const int MinimumReasonLength = 3;
    private const int MaximumReasonLength = 500;
    private const int MaximumConsumers = 20;
    private const int MaximumCodeLength = 64;

    private readonly List<string> _requiredConsumers;
    private readonly List<MergeConsumerAcknowledgement> _acknowledgements;

    private CustomerMergeRequest(
        InstitutionId institutionId,
        string mergeRequestId,
        string survivorCustomerId,
        string sourceCustomerId,
        string reason,
        string planHash,
        IReadOnlyList<string> requiredConsumers,
        IReadOnlyList<MergeConsumerAcknowledgement> acknowledgements,
        MergeRequestStatus status,
        string? approvalReference,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? completedAt)
    {
        InstitutionId = institutionId;
        MergeRequestId = mergeRequestId;
        SurvivorCustomerId = survivorCustomerId;
        SourceCustomerId = sourceCustomerId;
        Reason = reason;
        PlanHash = planHash;
        _requiredConsumers = [.. requiredConsumers];
        _acknowledgements = [.. acknowledgements];
        Status = status;
        ApprovalReference = approvalReference;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        CompletedAt = completedAt;
    }

    public InstitutionId InstitutionId { get; }

    public string MergeRequestId { get; }

    public string SurvivorCustomerId { get; }

    public string SourceCustomerId { get; }

    public string Reason { get; }

    /// <summary>SHA-256 over survivor, source, reason and the ordered required consumers. Cited by the event.</summary>
    public string PlanHash { get; }

    public IReadOnlyList<string> RequiredConsumers => _requiredConsumers;

    public IReadOnlyList<MergeConsumerAcknowledgement> Acknowledgements => _acknowledgements;

    public MergeRequestStatus Status { get; private set; }

    public string? ApprovalReference { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<string> OutstandingConsumers =>
        [.. _requiredConsumers.Where(consumer => !_acknowledgements.Any(acknowledgement =>
            string.Equals(acknowledgement.Consumer, consumer, StringComparison.Ordinal)))];

    public static CustomerMergeRequest Propose(
        InstitutionId institutionId,
        string survivorCustomerId,
        string sourceCustomerId,
        string reason,
        IReadOnlyList<string> requiredConsumers,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(survivorCustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCustomerId);
        ArgumentNullException.ThrowIfNull(requiredConsumers);

        if (string.Equals(survivorCustomerId.Trim(), sourceCustomerId.Trim(), StringComparison.Ordinal))
        {
            throw new OnboardingRuleException("merge_same_customer", "A merge requires two different customers.");
        }

        string trimmedReason = (reason ?? string.Empty).Trim();
        if (trimmedReason.Length < MinimumReasonLength || trimmedReason.Length > MaximumReasonLength)
        {
            throw new OnboardingRuleException("merge_reason_required", "A merge proposal requires a recorded reason.");
        }

        if (requiredConsumers.Count == 0 || requiredConsumers.Count > MaximumConsumers)
        {
            throw new OnboardingRuleException("merge_consumers_invalid", "A merge requires a bounded, non-empty consumer list.");
        }

        List<string> consumers = [];
        foreach (string consumer in requiredConsumers)
        {
            string code = ValidateCode(consumer, "merge_consumers_invalid");
            if (consumers.Contains(code, StringComparer.Ordinal))
            {
                throw new OnboardingRuleException("merge_consumers_invalid", "A consumer may appear only once on a merge plan.");
            }

            consumers.Add(code);
        }

        string hash = ComputePlanHash(survivorCustomerId.Trim(), sourceCustomerId.Trim(), trimmedReason, consumers);

        return new CustomerMergeRequest(
            institutionId,
            Guid.NewGuid().ToString("N"),
            survivorCustomerId.Trim(),
            sourceCustomerId.Trim(),
            trimmedReason,
            hash,
            consumers,
            acknowledgements: [],
            MergeRequestStatus.Proposed,
            approvalReference: null,
            now,
            now,
            completedAt: null);
    }

    public static CustomerMergeRequest Restore(
        InstitutionId institutionId,
        string mergeRequestId,
        string survivorCustomerId,
        string sourceCustomerId,
        string reason,
        string planHash,
        IReadOnlyList<string> requiredConsumers,
        IReadOnlyList<MergeConsumerAcknowledgement> acknowledgements,
        MergeRequestStatus status,
        string? approvalReference,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? completedAt) =>
        new(
            institutionId,
            mergeRequestId,
            survivorCustomerId,
            sourceCustomerId,
            reason,
            planHash,
            requiredConsumers,
            acknowledgements,
            status,
            approvalReference,
            createdAt,
            updatedAt,
            completedAt);

    /// <summary>
    /// SHA-256 over the frozen plan form: survivor, source, reason and the ordered consumer list. A changed
    /// consumer list or a different reason produces a different hash, so it can never be applied by accident.
    /// </summary>
    public static string ComputePlanHash(
        string survivorCustomerId,
        string sourceCustomerId,
        string reason,
        IReadOnlyList<string> requiredConsumers)
    {
        ArgumentNullException.ThrowIfNull(requiredConsumers);

        string canonical = string.Join(
            '|',
            survivorCustomerId.Trim(),
            sourceCustomerId.Trim(),
            reason.Trim(),
            string.Join(',', requiredConsumers));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public void Analyse(DateTimeOffset now)
    {
        Transition(MergeRequestStatus.Analysed, now, [MergeRequestStatus.Proposed]);
    }

    public void SubmitForApproval(DateTimeOffset now)
    {
        Transition(MergeRequestStatus.PendingApproval, now, [MergeRequestStatus.Analysed]);
    }

    public void Approve(string workflowDecisionReference, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDecisionReference);

        Transition(MergeRequestStatus.Approved, now, [MergeRequestStatus.PendingApproval]);
        ApprovalReference = workflowDecisionReference.Trim();
    }

    /// <summary>Starts the local apply. The plan is frozen from this point; only acknowledgements may be added.</summary>
    public void BeginApplying(DateTimeOffset now)
    {
        Transition(MergeRequestStatus.Applying, now, [MergeRequestStatus.Approved]);
    }

    /// <summary>Records one consumer acknowledgement. Repeating it is idempotent.</summary>
    public void Acknowledge(string consumer, DateTimeOffset now)
    {
        string code = ValidateCode(consumer, "merge_consumer_invalid");
        if (!_requiredConsumers.Contains(code, StringComparer.Ordinal))
        {
            throw new OnboardingRuleException("merge_consumer_not_required", "The consumer is not part of this merge plan.");
        }

        if (Status is not (MergeRequestStatus.Applying or MergeRequestStatus.ReconciliationRequired))
        {
            throw new OnboardingRuleException("merge_status_invalid", "Acknowledgements are accepted only while the merge is applying.");
        }

        if (_acknowledgements.Any(acknowledgement => string.Equals(acknowledgement.Consumer, code, StringComparison.Ordinal)))
        {
            return;
        }

        _acknowledgements.Add(new MergeConsumerAcknowledgement(code, now));
        UpdatedAt = now;

        if (OutstandingConsumers.Count == 0)
        {
            Status = MergeRequestStatus.Completed;
            CompletedAt = now;
        }
    }

    /// <summary>
    /// Marks the merge as awaiting convergence when applying finished but consumers have not all answered.
    /// Completion still requires every required acknowledgement.
    /// </summary>
    public void RequireReconciliation(DateTimeOffset now)
    {
        if (Status != MergeRequestStatus.Applying)
        {
            throw new OnboardingRuleException("merge_status_invalid", "Only an applying merge can require reconciliation.");
        }

        if (OutstandingConsumers.Count == 0)
        {
            throw new OnboardingRuleException("merge_status_invalid", "Nothing is outstanding for reconciliation.");
        }

        Status = MergeRequestStatus.ReconciliationRequired;
        UpdatedAt = now;
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        if (Status is MergeRequestStatus.Approved or MergeRequestStatus.Applying or MergeRequestStatus.ReconciliationRequired)
        {
            throw new OnboardingRuleException("merge_status_invalid", "An approved merge cannot be rejected; withdraw it instead.");
        }

        string trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length < MinimumReasonLength || trimmed.Length > MaximumReasonLength)
        {
            throw new OnboardingRuleException("merge_reason_required", "A rejection requires a recorded reason.");
        }

        Transition(MergeRequestStatus.Rejected, now, [MergeRequestStatus.Proposed, MergeRequestStatus.Analysed, MergeRequestStatus.PendingApproval]);
    }

    public void Withdraw(string reason, DateTimeOffset now)
    {
        string trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length < MinimumReasonLength || trimmed.Length > MaximumReasonLength)
        {
            throw new OnboardingRuleException("merge_reason_required", "A withdrawal requires a recorded reason.");
        }

        if (Status is MergeRequestStatus.Completed or MergeRequestStatus.Rejected or MergeRequestStatus.Withdrawn)
        {
            throw new OnboardingRuleException("merge_status_invalid", "A closed merge cannot be withdrawn.");
        }

        Status = MergeRequestStatus.Withdrawn;
        UpdatedAt = now;
    }

    private void Transition(MergeRequestStatus target, DateTimeOffset now, MergeRequestStatus[] allowedFrom)
    {
        if (!allowedFrom.Contains(Status))
        {
            throw new OnboardingRuleException(
                "merge_status_invalid",
                $"The merge transition from '{MergeCodes.ToCode(Status)}' to '{MergeCodes.ToCode(target)}' is not allowed.");
        }

        Status = target;
        UpdatedAt = now;
    }

    private static string ValidateCode(string value, string errorCode)
    {
        string trimmed = (value ?? string.Empty).Trim();
        bool valid = trimmed.Length is > 1 and <= MaximumCodeLength
            && char.IsAsciiLetter(trimmed[0])
            && trimmed.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

        return valid
            ? trimmed.ToLowerInvariant()
            : throw new OnboardingRuleException(errorCode, "A merge consumer must be a stable code supplied by approved configuration.");
    }
}
