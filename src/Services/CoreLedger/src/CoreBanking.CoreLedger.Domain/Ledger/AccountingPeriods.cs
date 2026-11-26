using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreBanking.CoreLedger.Domain.Ledger;

/// <summary>The approved accounting-period lifecycle. Decision: P1-E05-D13.</summary>
public enum AccountingPeriodStatus
{
    Open,
    SoftClosed,
    Closed,
    Locked,
    Reopened,
}

/// <summary>One recorded approval of a period transition. Decision: P1-E05-D13.</summary>
public sealed record AccountingPeriodApproval(
    string ApproverActorId,
    string ApprovalReference,
    DateTimeOffset ApprovedAt);

/// <summary>
/// The approved period-state rules: which transitions are legal, what may post in each state and what authority a
/// close or reopen needs. Decision: P1-E05-D13.
/// </summary>
public static class AccountingPeriodRules
{
    /// <summary>A reopen is time-bounded. Decision: P1-E05-D13.</summary>
    public static readonly TimeSpan MaximumReopenWindow = TimeSpan.FromDays(5);

    public static bool IsPostable(
        AccountingPeriodStatus status,
        IEnumerable<string> allowedEventCodes,
        string eventCode)
    {
        ArgumentNullException.ThrowIfNull(allowedEventCodes);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventCode);

        return status switch
        {
            AccountingPeriodStatus.Open => true,
            AccountingPeriodStatus.SoftClosed or AccountingPeriodStatus.Reopened =>
                Contains(allowedEventCodes, eventCode),
            _ => false,
        };
    }

    public static void EnsureTransition(AccountingPeriodStatus from, AccountingPeriodStatus to)
    {
        bool legal = (from, to) switch
        {
            (AccountingPeriodStatus.Open, AccountingPeriodStatus.SoftClosed) => true,
            (AccountingPeriodStatus.Open, AccountingPeriodStatus.Closed) => true,
            (AccountingPeriodStatus.SoftClosed, AccountingPeriodStatus.Open) => true,
            (AccountingPeriodStatus.SoftClosed, AccountingPeriodStatus.Closed) => true,
            (AccountingPeriodStatus.Closed, AccountingPeriodStatus.Reopened) => true,
            (AccountingPeriodStatus.Closed, AccountingPeriodStatus.Locked) => true,
            (AccountingPeriodStatus.Reopened, AccountingPeriodStatus.Closed) => true,
            _ => false,
        };
        if (!legal)
        {
            throw new LedgerPostingException("accounting_period_transition_invalid");
        }
    }

    /// <summary>Soft-close and reopen are allow-list driven; an empty list posts nothing. Decision: P1-E05-D13.</summary>
    public static string[] NormaliseAllowList(IEnumerable<string>? eventCodes)
    {
        if (eventCodes is null)
        {
            return [];
        }

        string[] codes = eventCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .ToArray();
        if (codes.Length != codes.Distinct(StringComparer.Ordinal).Count())
        {
            throw new LedgerPostingException("accounting_period_allow_list_duplicate");
        }

        return codes;
    }

    public static void EnsureCloseEvidence(AccountingPeriodCloseEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!evidence.RequiredProcessingStepsCompleted || !evidence.OutboxHealthy)
        {
            throw new LedgerPostingException("accounting_period_processing_incomplete");
        }

        if (evidence.BlockingReconciliationExceptions > 0)
        {
            throw new LedgerPostingException("accounting_period_reconciliation_blocked");
        }

        if (evidence.DebitTotal != evidence.CreditTotal)
        {
            throw new LedgerPostingException("accounting_period_unbalanced");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(evidence.ApprovalReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence.SummaryHash);
    }

    public static void EnsureDistinctApprovers(
        string makerActorId,
        IReadOnlyList<AccountingPeriodApproval> approvals,
        int required)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(makerActorId);
        ArgumentNullException.ThrowIfNull(approvals);

        HashSet<string> distinct = new(StringComparer.Ordinal);
        foreach (AccountingPeriodApproval approval in approvals)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(approval.ApproverActorId);
            ArgumentException.ThrowIfNullOrWhiteSpace(approval.ApprovalReference);
            if (string.Equals(approval.ApproverActorId.Trim(), makerActorId.Trim(), StringComparison.Ordinal))
            {
                throw new LedgerPostingException("accounting_period_self_approval_denied");
            }

            distinct.Add(approval.ApproverActorId.Trim());
        }

        if (distinct.Count < required)
        {
            throw new LedgerPostingException("accounting_period_approval_insufficient");
        }
    }

    public static void EnsureReopenWindow(DateTimeOffset reopenedAt, DateTimeOffset expiresAt)
    {
        if (expiresAt <= reopenedAt)
        {
            throw new LedgerPostingException("accounting_period_reopen_expiry_invalid");
        }

        if (expiresAt - reopenedAt > MaximumReopenWindow)
        {
            throw new LedgerPostingException("accounting_period_reopen_expiry_too_long");
        }
    }

    private static bool Contains(System.Collections.Generic.IEnumerable<string> eventCodes, string eventCode) =>
        System.Linq.Enumerable.Contains(eventCodes, eventCode.Trim(), StringComparer.Ordinal);
}

/// <summary>The immutable close evidence a balanced, reconciled period must supply. Decision: P1-E05-D13.</summary>
public sealed record AccountingPeriodCloseEvidence(
    decimal DebitTotal,
    decimal CreditTotal,
    int BlockingReconciliationExceptions,
    bool RequiredProcessingStepsCompleted,
    bool OutboxHealthy,
    string ApprovalReference,
    string SummaryHash);
