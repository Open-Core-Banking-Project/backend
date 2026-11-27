using System;

namespace CoreBanking.CoreLedger.Domain.Ledger;

/// <summary>How a value date relates to the business date. Decision: P1-E05-D12.</summary>
public enum ValueDateClassification
{
    Ordinary,
    HistoricalPriorPeriod,
}

/// <summary>The exceptional backdate authority a historical value date needs. Decision: P1-E05-D12.</summary>
public sealed record DateAuthority(
    string ApprovalReference,
    string ReasonCode,
    string EvidenceReference,
    int MaximumAgeDays,
    bool StepUpVerified);

public sealed record PostingDateDecision(
    bool Allowed,
    string? RefusalCode,
    ValueDateClassification Classification,
    int AgeDays,
    bool RequiresRecalculationHandoff)
{
    public static PostingDateDecision Refused(string code) =>
        new(false, code, ValueDateClassification.Ordinary, 0, false);

    public static PostingDateDecision Ordinary(int ageDays) =>
        new(true, null, ValueDateClassification.Ordinary, ageDays, false);

    public static PostingDateDecision Historical(int ageDays) =>
        new(true, null, ValueDateClassification.HistoricalPriorPeriod, ageDays, true);
}

/// <summary>
/// The approved financial date policy: the business date comes from the institution calendar, the posting date is the
/// current open period, and a value date is ordinary inside 90 days and historical up to 365 days under an exact
/// backdate authority. Future dates and anything older are refused. Decision: P1-E05-D12.
/// </summary>
public static class FinancialDatePolicy
{
    public const int OrdinaryWindowDays = 90;
    public const int HistoricalWindowDays = 365;

    public static PostingDateDecision Evaluate(
        DateOnly businessDate,
        DateOnly postingDate,
        DateOnly valueDate,
        DateAuthority? authority)
    {
        if (postingDate != businessDate)
        {
            return PostingDateDecision.Refused("posting_date_business_date_mismatch");
        }

        if (valueDate > businessDate)
        {
            return PostingDateDecision.Refused("value_date_future_denied");
        }

        int ageDays = businessDate.DayNumber - valueDate.DayNumber;
        if (ageDays <= OrdinaryWindowDays)
        {
            return PostingDateDecision.Ordinary(ageDays);
        }

        if (ageDays > HistoricalWindowDays)
        {
            return PostingDateDecision.Refused("value_date_too_old");
        }

        if (authority is null
            || !authority.StepUpVerified
            || string.IsNullOrWhiteSpace(authority.ApprovalReference)
            || string.IsNullOrWhiteSpace(authority.ReasonCode)
            || string.IsNullOrWhiteSpace(authority.EvidenceReference)
            || authority.MaximumAgeDays < ageDays
            || authority.MaximumAgeDays > HistoricalWindowDays)
        {
            return PostingDateDecision.Refused("backdate_authority_required");
        }

        return PostingDateDecision.Historical(ageDays);
    }
}
