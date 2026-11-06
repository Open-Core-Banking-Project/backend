using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreBanking.CoreLedger.Domain.Accounts;

public enum AccountApplicationStatus
{
    Draft,
    EligibilityPending,
    Eligible,
    Ineligible,
    PendingApproval,
    Approved,
    Rejected,
    CancelledBeforeActivation,
    Open,
    Active,
}

/// <summary>
/// The approved application state machine. Slice 7 owns the transitions up to eligibility; the
/// approval, opening and activation transitions belong to P1-E04-S9. Decision: P1-E04-D09.
/// </summary>
public static class AccountApplicationLifecycle
{
    private static readonly HashSet<AccountApplicationStatus> CountedStatuses =
    [
        AccountApplicationStatus.Draft,
        AccountApplicationStatus.EligibilityPending,
        AccountApplicationStatus.Eligible,
        AccountApplicationStatus.PendingApproval,
        AccountApplicationStatus.Approved,
        AccountApplicationStatus.Open,
        AccountApplicationStatus.Active,
    ];

    /// <summary>
    /// In-flight and open applications count against a multiple-account limit so an application
    /// awaiting approval cannot be duplicated. Decision: P1-E04-D07.
    /// </summary>
    public static bool CountsTowardsLimit(AccountApplicationStatus status) => CountedStatuses.Contains(status);

    public static AccountApplicationStatus BeginEligibilityEvaluation(AccountApplicationStatus current) =>
        current == AccountApplicationStatus.Draft
            ? AccountApplicationStatus.EligibilityPending
            : throw new InvalidOperationException($"account_application_transition_not_allowed:{current}");

    public static AccountApplicationStatus RecordEligibility(AccountApplicationStatus current, bool eligible)
    {
        if (current != AccountApplicationStatus.EligibilityPending)
            throw new InvalidOperationException($"account_application_transition_not_allowed:{current}");
        return eligible ? AccountApplicationStatus.Eligible : AccountApplicationStatus.Ineligible;
    }
}
