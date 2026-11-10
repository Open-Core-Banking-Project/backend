using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CoreBanking.CoreLedger.Domain.Institutions;

namespace CoreBanking.CoreLedger.Domain.Accounts;

public enum DormancyDecision
{
    NotDormant,
    NoticeDue,
    Dormant,
    NotAssessable,
}

/// <summary>One allow-listed qualifying activity: a posted, customer-initiated transaction type and direction.</summary>
public sealed record DormancyQualifyingActivity(string ActivityCode, string DirectionCode);

/// <summary>
/// One movement as the dormancy assessment reads it. The source is the ledger's posted movement history by
/// business date; nothing here is derived from a summary, a cache or a channel log. Decision: P1-E04-D12.
/// </summary>
public sealed record AccountMovementDescriptor(
    string ActivityCode,
    string DirectionCode,
    DateOnly BusinessDate,
    bool Posted,
    bool BankInitiated);

/// <summary>
/// The institution's effective-dated <c>dormancy.rules</c> content: the threshold in days, the notice lead
/// time, whether reactivation needs approval and the qualifying-activity allow-list. No default threshold is
/// shipped: a product without a published rule is reported as unassessable, never assumed. Decision: P1-E04-D12.
/// </summary>
public sealed record DormancyRule(
    string RuleId,
    int Version,
    int ThresholdDays,
    int NoticeLeadDays,
    bool ReactivationRequiresApproval,
    IReadOnlyList<DormancyQualifyingActivity> QualifyingActivities)
{
    public const string Area = "dormancy.rules";
    public const string Schema = "dormancy.rules.v1";

    private const int MaximumThresholdDays = 3650;

    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        "ruleId", "version", "thresholdDays", "noticeLeadDays", "reactivationRequiresApproval", "qualifyingActivities",
    };

    private static readonly HashSet<string> AllowedActivityProperties = new(StringComparer.Ordinal)
    {
        "activityCode", "direction",
    };

    private static readonly string[] AllowedDirections = ["debit", "credit", "both"];

    /// <summary>The business date on which a pre-dormancy notice first becomes due.</summary>
    public int NoticeStartDay => ThresholdDays - NoticeLeadDays;

    public bool Qualifies(string activityCode, string directionCode) =>
        QualifyingActivities.Any(activity =>
            string.Equals(activity.ActivityCode, activityCode?.Trim(), StringComparison.Ordinal)
            && (string.Equals(activity.DirectionCode, "both", StringComparison.Ordinal)
                || string.Equals(activity.DirectionCode, directionCode?.Trim().ToLowerInvariant(), StringComparison.Ordinal)));

    public ConfigurationContent ToConfigurationContent() => ConfigurationContent.Create(JsonSerializer.Serialize(new
    {
        ruleId = RuleId,
        version = Version,
        thresholdDays = ThresholdDays,
        noticeLeadDays = NoticeLeadDays,
        reactivationRequiresApproval = ReactivationRequiresApproval,
        qualifyingActivities = QualifyingActivities
            .Select(activity => new { activityCode = activity.ActivityCode, direction = activity.DirectionCode })
            .ToArray(),
    }));

    public static DormancyRule Create(
        string ruleId,
        int version,
        int thresholdDays,
        int noticeLeadDays,
        bool reactivationRequiresApproval,
        IEnumerable<DormancyQualifyingActivity> qualifyingActivities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        if (ruleId.Trim().Length > 64)
            throw new ArgumentException("A rule identifier may contain at most 64 characters.", nameof(ruleId));
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        if (thresholdDays < 1 || thresholdDays > MaximumThresholdDays)
            throw new ArgumentOutOfRangeException(
                nameof(thresholdDays),
                $"A dormancy threshold must be between 1 and {MaximumThresholdDays} days.");
        if (noticeLeadDays < 0 || noticeLeadDays > thresholdDays)
            throw new ArgumentOutOfRangeException(
                nameof(noticeLeadDays),
                "A notice lead time cannot be negative or longer than the threshold.");

        DormancyQualifyingActivity[] activities = [.. (qualifyingActivities
            ?? throw new ArgumentNullException(nameof(qualifyingActivities)))
            .Select(activity => ValidateActivity(activity))];
        if (activities.Length == 0)
            throw new ArgumentException("A dormancy rule requires at least one qualifying activity.", nameof(qualifyingActivities));
        if (activities.Select(activity => activity.ActivityCode).Distinct(StringComparer.Ordinal).Count() != activities.Length)
            throw new ArgumentException("A qualifying activity may appear only once.", nameof(qualifyingActivities));

        return new DormancyRule(ruleId.Trim(), version, thresholdDays, noticeLeadDays, reactivationRequiresApproval, activities);
    }

    public static DormancyRule Parse(string contentJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentJson);

        using JsonDocument document = JsonDocument.Parse(contentJson);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Any(property => !AllowedProperties.Contains(property.Name)))
            throw new ArgumentException("The dormancy rule contains unsupported properties.", nameof(contentJson));

        foreach (string required in AllowedProperties)
        {
            if (!root.TryGetProperty(required, out _))
                throw new ArgumentException("The dormancy rule is missing a required property.", nameof(contentJson));
        }

        if (root.GetProperty("qualifyingActivities").ValueKind != JsonValueKind.Array)
            throw new ArgumentException("The qualifying activities must be an array.", nameof(contentJson));

        List<DormancyQualifyingActivity> activities = [];
        foreach (JsonElement element in root.GetProperty("qualifyingActivities").EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || element.EnumerateObject().Any(property => !AllowedActivityProperties.Contains(property.Name)))
                throw new ArgumentException("A qualifying activity contains unsupported properties.", nameof(contentJson));
            activities.Add(new DormancyQualifyingActivity(
                element.GetProperty("activityCode").GetString() ?? string.Empty,
                element.GetProperty("direction").GetString() ?? string.Empty));
        }

        return Create(
            root.GetProperty("ruleId").GetString() ?? string.Empty,
            root.GetProperty("version").GetInt32(),
            root.GetProperty("thresholdDays").GetInt32(),
            root.GetProperty("noticeLeadDays").GetInt32(),
            root.GetProperty("reactivationRequiresApproval").GetBoolean(),
            activities);
    }

    private static DormancyQualifyingActivity ValidateActivity(DormancyQualifyingActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentException.ThrowIfNullOrWhiteSpace(activity.ActivityCode);
        string code = activity.ActivityCode.Trim().ToLowerInvariant();
        if (code.Length > 64)
            throw new ArgumentException("An activity code may contain at most 64 characters.", nameof(activity));
        string direction = activity.DirectionCode?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!AllowedDirections.Contains(direction, StringComparer.Ordinal))
            throw new ArgumentException("A qualifying activity direction must be debit, credit or both.", nameof(activity));
        return new DormancyQualifyingActivity(code, direction);
    }
}

/// <summary>The outcome of one assessment, including the dates considered and the rule that decided it.</summary>
public sealed record DormancyAssessmentOutcome(
    DormancyDecision Decision,
    bool TransitionPermitted,
    bool NoticeDue,
    IReadOnlyList<string> ReasonCodes,
    DateOnly BusinessDate,
    DateOnly ClockOriginDate,
    DateOnly? LastQualifyingActivityDate,
    int InactivityDays,
    int ThresholdDays,
    int NoticeLeadDays)
{
    public bool Dormant => Decision == DormancyDecision.Dormant;
}

/// <summary>
/// The approved dormancy assessment: a pure function over the last qualifying activity, the business date and
/// the rule version, so a replay of the same inputs always produces the same outcome. Wall-clock time is never
/// used, a read never resets the clock, and a missing notice refuses the transition. Decision: P1-E04-D12.
/// </summary>
public static class AccountDormancyPolicy
{
    public const string ThresholdNotReachedCode = "dormancy_threshold_not_reached";
    public const string NoticeDueCode = "dormancy_notice_due";
    public const string NoticeMissingCode = "dormancy_notice_missing";
    public const string ThresholdReachedCode = "dormancy_threshold_reached";
    public const string RuleMissingCode = "dormancy_rule_missing";
    public const string ActivitySourceUnavailableCode = "dormancy_activity_source_unavailable";
    public const string AssessmentFailedCode = "dormancy_assessment_failed";
    public const string BusinessDateBeforeOriginCode = "dormancy_business_date_before_origin";
    public const string ReactivationRequiresApprovalCode = "dormancy_reactivation_requires_approval";
    public const string ReactivationDualControlCode = "dormancy_reactivation_dual_control_required";
    public const string ReactivationNotDormantCode = "dormancy_reactivation_not_dormant";

    /// <summary>
    /// Access, bank-initiated entries and control changes can never keep an account alive, whatever an
    /// allow-list says. Decision: P1-E04-D12.
    /// </summary>
    public static IReadOnlySet<string> ProhibitedActivityCodes { get; } = new HashSet<string>(
        [
            "balance_enquiry",
            "statement_request",
            "login",
            "interest_capitalisation",
            "fee",
            "charge",
            "tax",
            "bank_correction",
            "bank_reversal",
            "lien_change",
            "control_change",
            "failed_posting",
        ],
        StringComparer.Ordinal);

    public static bool IsQualifying(AccountMovementDescriptor movement, DormancyRule rule)
    {
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentNullException.ThrowIfNull(rule);
        if (!movement.Posted || movement.BankInitiated)
        {
            return false;
        }

        string code = movement.ActivityCode?.Trim().ToLowerInvariant() ?? string.Empty;
        return !ProhibitedActivityCodes.Contains(code) && rule.Qualifies(code, movement.DirectionCode);
    }

    /// <summary>The most recent qualifying posting date, or null when the account has never qualified.</summary>
    public static DateOnly? LastQualifyingActivity(
        IEnumerable<AccountMovementDescriptor> movements,
        DormancyRule rule)
    {
        ArgumentNullException.ThrowIfNull(movements);
        DateOnly? latest = null;
        foreach (AccountMovementDescriptor movement in movements)
        {
            if (!IsQualifying(movement, rule))
            {
                continue;
            }

            if (latest is null || movement.BusinessDate > latest.Value)
            {
                latest = movement.BusinessDate;
            }
        }

        return latest;
    }

    /// <summary>
    /// Missing assessments are caught up in order, oldest first, so the oldest missed date is never skipped.
    /// Decision: P1-E04-D12.
    /// </summary>
    public static IReadOnlyList<DateOnly> OrderForCatchUp(IEnumerable<DateOnly> businessDates)
    {
        ArgumentNullException.ThrowIfNull(businessDates);
        return [.. businessDates.Distinct().OrderBy(date => date)];
    }

    public static DormancyAssessmentOutcome Assess(
        DateOnly businessDate,
        DateOnly? lastQualifyingActivityDate,
        DateOnly clockOriginDate,
        DormancyRule rule,
        bool noticeRecorded)
    {
        ArgumentNullException.ThrowIfNull(rule);

        DateOnly origin = lastQualifyingActivityDate ?? clockOriginDate;
        int inactivityDays = businessDate.DayNumber - origin.DayNumber;
        if (inactivityDays < 0)
        {
            return new DormancyAssessmentOutcome(
                DormancyDecision.NotAssessable,
                TransitionPermitted: false,
                NoticeDue: false,
                [BusinessDateBeforeOriginCode],
                businessDate,
                clockOriginDate,
                lastQualifyingActivityDate,
                inactivityDays,
                rule.ThresholdDays,
                rule.NoticeLeadDays);
        }

        bool noticeDue = rule.NoticeLeadDays > 0 && inactivityDays >= rule.NoticeStartDay;
        if (inactivityDays < rule.ThresholdDays)
        {
            if (noticeDue && !noticeRecorded)
            {
                return new DormancyAssessmentOutcome(
                    DormancyDecision.NoticeDue,
                    TransitionPermitted: false,
                    NoticeDue: true,
                    [NoticeDueCode],
                    businessDate,
                    clockOriginDate,
                    lastQualifyingActivityDate,
                    inactivityDays,
                    rule.ThresholdDays,
                    rule.NoticeLeadDays);
            }

            return new DormancyAssessmentOutcome(
                noticeDue ? DormancyDecision.NoticeDue : DormancyDecision.NotDormant,
                TransitionPermitted: false,
                noticeDue,
                [noticeDue ? NoticeDueCode : ThresholdNotReachedCode],
                businessDate,
                clockOriginDate,
                lastQualifyingActivityDate,
                inactivityDays,
                rule.ThresholdDays,
                rule.NoticeLeadDays);
        }

        if (noticeDue && !noticeRecorded)
        {
            // Where the policy requires notice, a transition without recorded notice is refused.
            return new DormancyAssessmentOutcome(
                DormancyDecision.Dormant,
                TransitionPermitted: false,
                NoticeDue: true,
                [NoticeMissingCode, ThresholdReachedCode],
                businessDate,
                clockOriginDate,
                lastQualifyingActivityDate,
                inactivityDays,
                rule.ThresholdDays,
                rule.NoticeLeadDays);
        }

        return new DormancyAssessmentOutcome(
            DormancyDecision.Dormant,
            TransitionPermitted: true,
            NoticeDue: noticeDue,
            [ThresholdReachedCode],
            businessDate,
            clockOriginDate,
            lastQualifyingActivityDate,
            inactivityDays,
            rule.ThresholdDays,
            rule.NoticeLeadDays);
    }

    /// <summary>Reactivation needs an approval reference and a different actor when the rule says so.</summary>
    public static IReadOnlyList<string> ReactivationRefusals(
        DormancyRule rule,
        bool approvalReferencePresent,
        bool makerEqualsReleaser)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (!rule.ReactivationRequiresApproval)
        {
            return [];
        }

        List<string> reasons = [];
        if (!approvalReferencePresent)
        {
            reasons.Add(ReactivationRequiresApprovalCode);
        }

        if (makerEqualsReleaser)
        {
            reasons.Add(ReactivationDualControlCode);
        }

        return reasons;
    }
}
