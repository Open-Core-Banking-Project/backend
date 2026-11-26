using System;
using System.Linq;

namespace CoreBanking.CoreLedger.Domain.Ledger;

public enum ReversalFinalityEvidence
{
    LedgerPosted,
    ProviderUncertain,
}

public sealed record ReversalApprovalEvidence(
    string WorkflowDecisionId,
    string MakerActorId,
    string ApproverActorId,
    string ApprovedRequestHash,
    DateTimeOffset ApprovedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>Approved P1-E05-D10 full-reversal invariant checks.</summary>
public static class FinancialReversalPolicy
{
    public static void ValidateFull(
        JournalEntry original,
        JournalEntry reversal,
        string reasonCode,
        string evidenceReference,
        string requestHash,
        ReversalFinalityEvidence finalityEvidence,
        ReversalApprovalEvidence? approval,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(reversal);
        Required(reasonCode, nameof(reasonCode));
        Required(evidenceReference, nameof(evidenceReference));

        if (finalityEvidence != ReversalFinalityEvidence.LedgerPosted)
        {
            throw new LedgerPostingException("financial_reversal_original_not_final");
        }

        if (!string.Equals(original.MappingVersionId, reversal.MappingVersionId, StringComparison.Ordinal)
            || original.Scope != reversal.Scope
            || original.TotalDebits != reversal.TotalDebits
            || original.TotalCredits != reversal.TotalCredits
            || original.Lines.Count != reversal.Lines.Count)
        {
            throw new LedgerPostingException("financial_reversal_not_exact_opposite");
        }

        foreach (JournalLine originalLine in original.Lines)
        {
            JournalLine? reversalLine = reversal.Lines.SingleOrDefault(line => line.Sequence == originalLine.Sequence);
            if (reversalLine is null
                || !string.Equals(reversalLine.LedgerAccountId, originalLine.LedgerAccountId, StringComparison.Ordinal)
                || reversalLine.Direction == originalLine.Direction
                || reversalLine.Component != originalLine.Component
                || reversalLine.Amount != originalLine.Amount
                || reversalLine.Scope != originalLine.Scope)
            {
                throw new LedgerPostingException("financial_reversal_not_exact_opposite");
            }
        }

        if (approval is null)
        {
            return;
        }

        Required(approval.WorkflowDecisionId, nameof(approval.WorkflowDecisionId));
        Required(approval.MakerActorId, nameof(approval.MakerActorId));
        Required(approval.ApproverActorId, nameof(approval.ApproverActorId));
        if (string.Equals(approval.MakerActorId, approval.ApproverActorId, StringComparison.Ordinal))
        {
            throw new LedgerPostingException("financial_reversal_self_approval_denied");
        }

        if (!string.Equals(approval.ApprovedRequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new LedgerPostingException("financial_reversal_approval_mismatch");
        }

        if (approval.ExpiresAt is not null && evaluatedAt >= approval.ExpiresAt.Value)
        {
            throw new LedgerPostingException("financial_reversal_approval_expired");
        }
    }

    private static string Required(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }
}
