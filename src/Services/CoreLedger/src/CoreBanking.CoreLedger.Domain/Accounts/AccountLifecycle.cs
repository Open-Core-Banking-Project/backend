using System;
using System.Collections.Generic;

namespace CoreBanking.CoreLedger.Domain.Accounts;

/// <summary>
/// The approved account statuses. Dormancy, pending closure and closure are owned by P1-E04-D12 and
/// P1-E04-D13; the set matches the database check constraint exactly.
/// </summary>
public enum AccountStatus
{
    Open,
    Active,
    Restricted,
    Frozen,
    Dormant,
    PendingClosure,
    Closed,
}

/// <summary>
/// The approved account status transitions. Dormancy is entered from an active account and left only by
/// reactivation; closure always passes through <see cref="AccountStatus.PendingClosure"/> and a closed
/// account never reopens. Decisions: P1-E04-D12, P1-E04-D13.
/// </summary>
public static class AccountLifecycle
{
    public const string ClosedCode = "account_closed";
    public const string ReopeningNotPermittedCode = "account_closure_irreversible";

    private static readonly HashSet<AccountStatus> PostingStatuses =
    [
        AccountStatus.Open,
        AccountStatus.Active,
        AccountStatus.Restricted,
        AccountStatus.Frozen,
        AccountStatus.Dormant,
    ];

    public static string Token(AccountStatus status) => status.ToString();

    public static AccountStatus Parse(string status) =>
        Enum.TryParse(status, ignoreCase: false, out AccountStatus parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException($"account_status_unknown:{status}");

    /// <summary>Only an active account becomes dormant; a read or a login never changes a status. Decision: P1-E04-D12.</summary>
    public static AccountStatus MarkDormant(AccountStatus current) =>
        current == AccountStatus.Active
            ? AccountStatus.Dormant
            : throw new InvalidOperationException($"account_status_transition_not_allowed:{current}");

    /// <summary>Reactivation is the only way out of dormancy and clears dormancy alone. Decision: P1-E04-D12.</summary>
    public static AccountStatus Reactivate(AccountStatus current) =>
        current == AccountStatus.Dormant
            ? AccountStatus.Active
            : throw new InvalidOperationException($"account_status_transition_not_allowed:{current}");

    /// <summary>
    /// Approval of a closure proposal moves the account to pending closure so ordinary debits are denied
    /// before the final condition re-check. Decision: P1-E04-D13.
    /// </summary>
    public static AccountStatus ProposeClosure(AccountStatus current) => current switch
    {
        AccountStatus.Active => AccountStatus.PendingClosure,
        AccountStatus.Restricted => AccountStatus.PendingClosure,
        AccountStatus.Frozen => AccountStatus.PendingClosure,
        AccountStatus.Dormant => AccountStatus.PendingClosure,
        _ => throw new InvalidOperationException($"account_status_transition_not_allowed:{current}"),
    };

    /// <summary>The only path to <see cref="AccountStatus.Closed"/> is from pending closure. Decision: P1-E04-D13.</summary>
    public static AccountStatus Close(AccountStatus current) =>
        current == AccountStatus.PendingClosure
            ? AccountStatus.Closed
            : throw new InvalidOperationException($"account_status_transition_not_allowed:{current}");

    /// <summary>
    /// Reopening is not permitted in Phase 1: a returned customer opens a new account with a new number.
    /// Decision: P1-E04-D13.
    /// </summary>
    public static AccountStatus Reopen(AccountStatus current) =>
        current == AccountStatus.Closed
            ? throw new InvalidOperationException(ReopeningNotPermittedCode)
            : throw new InvalidOperationException($"account_status_transition_not_allowed:{current}");

    /// <summary>An account that may still receive ordinary postings; a dormant account is not one of them for debits.</summary>
    public static bool AdmitsOrdinaryPosting(AccountStatus status) => PostingStatuses.Contains(status);
}
