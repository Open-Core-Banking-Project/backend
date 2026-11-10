using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Accounts;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

/// <summary>
/// The approved dormancy policy (P1-E04-D12): a pure decision over the last qualifying activity, the institution
/// business date and the rule version, with no shipped default threshold and no wall-clock input.
/// </summary>
public sealed class AccountDormancyTests
{
    private static readonly DateOnly Origin = new(2026, 1, 1);

    private static DormancyRule Rule(
        int thresholdDays = 90,
        int noticeLeadDays = 14,
        bool reactivationRequiresApproval = true) =>
        DormancyRule.Create(
            "rule-1",
            1,
            thresholdDays,
            noticeLeadDays,
            reactivationRequiresApproval,
            [new DormancyQualifyingActivity("customer_transfer", "debit")]);

    private static AccountMovementDescriptor Movement(
        string code,
        DateOnly date,
        bool posted = true,
        bool bankInitiated = false,
        string direction = "debit") =>
        new(code, direction, date, posted, bankInitiated);

    [Fact]
    public void A_rule_round_trips_through_its_canonical_configuration_content()
    {
        DormancyRule rule = Rule();

        DormancyRule parsed = DormancyRule.Parse(rule.ToConfigurationContent().CanonicalJson);

        Assert.Equal(DormancyRule.Area, "dormancy.rules");
        Assert.Equal(DormancyRule.Schema, "dormancy.rules.v1");
        Assert.Equal(rule.RuleId, parsed.RuleId);
        Assert.Equal(rule.Version, parsed.Version);
        Assert.Equal(rule.ThresholdDays, parsed.ThresholdDays);
        Assert.Equal(rule.NoticeLeadDays, parsed.NoticeLeadDays);
        Assert.Equal(rule.ReactivationRequiresApproval, parsed.ReactivationRequiresApproval);
        Assert.Equal(rule.QualifyingActivities, parsed.QualifyingActivities);
        Assert.Equal(76, parsed.NoticeStartDay);
    }

    [Fact]
    public void A_rule_without_a_threshold_or_allow_list_is_refused_so_no_default_is_shipped()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(thresholdDays: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(thresholdDays: 90, noticeLeadDays: 91));
        Assert.Throws<ArgumentException>(() => DormancyRule.Create("rule-1", 1, 90, 0, false, []));
        Assert.Throws<ArgumentException>(() => DormancyRule.Create(
            "rule-1", 1, 90, 0, false, [new DormancyQualifyingActivity("customer_transfer", "sideways")]));
    }

    [Fact]
    public void A_stored_rule_with_an_unsupported_property_is_refused()
    {
        string json = """
            {"ruleId":"rule-1","version":1,"thresholdDays":90,"noticeLeadDays":0,
             "reactivationRequiresApproval":false,"defaultThresholdDays":30,"qualifyingActivities":[]}
            """;

        Assert.Throws<ArgumentException>(() => DormancyRule.Parse(json));
    }

    [Fact]
    public void A_below_threshold_assessment_is_not_dormant_and_changes_nothing()
    {
        DormancyAssessmentOutcome outcome = AccountDormancyPolicy.Assess(
            Origin.AddDays(30), null, Origin, Rule(), noticeRecorded: false);

        Assert.Equal(DormancyDecision.NotDormant, outcome.Decision);
        Assert.False(outcome.TransitionPermitted);
        Assert.False(outcome.NoticeDue);
        Assert.Equal([AccountDormancyPolicy.ThresholdNotReachedCode], outcome.ReasonCodes);
        Assert.Equal(30, outcome.InactivityDays);
    }

    [Fact]
    public void A_qualifying_posting_resets_the_clock_and_a_read_does_not()
    {
        DormancyRule rule = Rule();
        AccountMovementDescriptor[] movements =
        [
            Movement("customer_transfer", Origin.AddDays(80)),
            Movement("balance_enquiry", Origin.AddDays(85)),
            Movement("statement_request", Origin.AddDays(86)),
            Movement("login", Origin.AddDays(87)),
        ];

        DateOnly? last = AccountDormancyPolicy.LastQualifyingActivity(movements, rule);

        Assert.Equal(Origin.AddDays(80), last);
        DormancyAssessmentOutcome outcome = AccountDormancyPolicy.Assess(
            Origin.AddDays(89), last, Origin, rule, noticeRecorded: true);
        Assert.Equal(DormancyDecision.NotDormant, outcome.Decision);
    }

    [Fact]
    public void A_fee_or_an_interest_capitalisation_never_qualifies_even_if_an_allow_list_names_it()
    {
        DormancyRule rule = DormancyRule.Create(
            "rule-1", 1, 90, 0, false,
            [
                new DormancyQualifyingActivity("fee", "debit"),
                new DormancyQualifyingActivity("interest_capitalisation", "credit"),
                new DormancyQualifyingActivity("customer_transfer", "debit"),
            ]);

        Assert.True(AccountDormancyPolicy.ProhibitedActivityCodes.Contains("fee"));
        Assert.False(AccountDormancyPolicy.IsQualifying(Movement("fee", Origin.AddDays(10)), rule));
        Assert.False(AccountDormancyPolicy.IsQualifying(
            Movement("interest_capitalisation", Origin.AddDays(10), direction: "credit"), rule));
        Assert.False(AccountDormancyPolicy.IsQualifying(
            Movement("customer_transfer", Origin.AddDays(10), posted: false), rule));
        Assert.False(AccountDormancyPolicy.IsQualifying(
            Movement("customer_transfer", Origin.AddDays(10), bankInitiated: true), rule));
        Assert.True(AccountDormancyPolicy.IsQualifying(Movement("customer_transfer", Origin.AddDays(10)), rule));
    }

    [Fact]
    public void A_wrong_direction_never_qualifies()
    {
        DormancyRule rule = DormancyRule.Create(
            "rule-1", 1, 90, 0, false, [new DormancyQualifyingActivity("customer_transfer", "credit")]);

        Assert.False(AccountDormancyPolicy.IsQualifying(Movement("customer_transfer", Origin), rule));
        Assert.True(AccountDormancyPolicy.IsQualifying(Movement("customer_transfer", Origin, direction: "credit"), rule));
    }

    [Fact]
    public void Reaching_the_threshold_is_dormant_and_permits_the_transition()
    {
        DormancyAssessmentOutcome outcome = AccountDormancyPolicy.Assess(
            Origin.AddDays(90), null, Origin, Rule(noticeLeadDays: 0), noticeRecorded: false);

        Assert.Equal(DormancyDecision.Dormant, outcome.Decision);
        Assert.True(outcome.TransitionPermitted);
        Assert.Equal([AccountDormancyPolicy.ThresholdReachedCode], outcome.ReasonCodes);
    }

    [Fact]
    public void A_required_notice_that_was_never_recorded_refuses_the_transition()
    {
        DormancyAssessmentOutcome outcome = AccountDormancyPolicy.Assess(
            Origin.AddDays(90), null, Origin, Rule(noticeLeadDays: 14), noticeRecorded: false);

        Assert.Equal(DormancyDecision.Dormant, outcome.Decision);
        Assert.False(outcome.TransitionPermitted);
        Assert.Contains(AccountDormancyPolicy.NoticeMissingCode, outcome.ReasonCodes);

        DormancyAssessmentOutcome withNotice = AccountDormancyPolicy.Assess(
            Origin.AddDays(90), null, Origin, Rule(noticeLeadDays: 14), noticeRecorded: true);
        Assert.True(withNotice.TransitionPermitted);
    }

    [Fact]
    public void The_notice_becomes_due_before_the_threshold_and_never_transitions()
    {
        DormancyAssessmentOutcome outcome = AccountDormancyPolicy.Assess(
            Origin.AddDays(80), null, Origin, Rule(noticeLeadDays: 14), noticeRecorded: false);

        Assert.Equal(DormancyDecision.NoticeDue, outcome.Decision);
        Assert.False(outcome.TransitionPermitted);
        Assert.True(outcome.NoticeDue);
        Assert.Equal([AccountDormancyPolicy.NoticeDueCode], outcome.ReasonCodes);
    }

    [Fact]
    public void A_business_date_before_the_clock_origin_is_not_assessable()
    {
        DormancyAssessmentOutcome outcome = AccountDormancyPolicy.Assess(
            Origin.AddDays(-1), null, Origin, Rule(), noticeRecorded: false);

        Assert.Equal(DormancyDecision.NotAssessable, outcome.Decision);
        Assert.False(outcome.TransitionPermitted);
        Assert.Equal([AccountDormancyPolicy.BusinessDateBeforeOriginCode], outcome.ReasonCodes);
    }

    [Fact]
    public void The_same_inputs_always_produce_the_same_assessment_so_a_replay_is_deterministic()
    {
        DormancyRule rule = Rule();
        DormancyAssessmentOutcome first = AccountDormancyPolicy.Assess(
            Origin.AddDays(120), Origin.AddDays(10), Origin, rule, noticeRecorded: true);
        DormancyAssessmentOutcome second = AccountDormancyPolicy.Assess(
            Origin.AddDays(120), Origin.AddDays(10), Origin, rule, noticeRecorded: true);

        Assert.Equal(first.Decision, second.Decision);
        Assert.Equal(first.ReasonCodes, second.ReasonCodes);
        Assert.Equal(first.TransitionPermitted, second.TransitionPermitted);
        Assert.Equal(first.BusinessDate, second.BusinessDate);
        Assert.Equal(110, first.InactivityDays);
    }

    [Fact]
    public void Missed_assessments_are_caught_up_in_order_and_never_skip_the_oldest_date()
    {
        DateOnly[] ordered = [.. AccountDormancyPolicy.OrderForCatchUp(
            [Origin.AddDays(3), Origin, Origin.AddDays(2), Origin.AddDays(1), Origin.AddDays(2)])];

        Assert.Equal(
            [Origin, Origin.AddDays(1), Origin.AddDays(2), Origin.AddDays(3)],
            ordered);
    }

    [Fact]
    public void Reactivation_needs_an_approval_reference_and_a_different_actor_when_the_rule_says_so()
    {
        DormancyRule rule = Rule(reactivationRequiresApproval: true);

        Assert.Equal(
            [AccountDormancyPolicy.ReactivationRequiresApprovalCode, AccountDormancyPolicy.ReactivationDualControlCode],
            AccountDormancyPolicy.ReactivationRefusals(rule, approvalReferencePresent: false, makerEqualsReleaser: true));
        Assert.Equal(
            [AccountDormancyPolicy.ReactivationRequiresApprovalCode],
            AccountDormancyPolicy.ReactivationRefusals(rule, approvalReferencePresent: false, makerEqualsReleaser: false));
        Assert.Equal(
            [AccountDormancyPolicy.ReactivationDualControlCode],
            AccountDormancyPolicy.ReactivationRefusals(rule, approvalReferencePresent: true, makerEqualsReleaser: true));
        Assert.Empty(AccountDormancyPolicy.ReactivationRefusals(rule, approvalReferencePresent: true, makerEqualsReleaser: false));
        Assert.Empty(AccountDormancyPolicy.ReactivationRefusals(
            Rule(reactivationRequiresApproval: false), approvalReferencePresent: false, makerEqualsReleaser: true));
    }

    [Fact]
    public void Dormancy_is_entered_only_from_active_and_left_only_by_reactivation()
    {
        Assert.Equal(AccountStatus.Dormant, AccountLifecycle.MarkDormant(AccountStatus.Active));
        Assert.Throws<InvalidOperationException>(() => AccountLifecycle.MarkDormant(AccountStatus.Dormant));
        Assert.Equal(AccountStatus.Active, AccountLifecycle.Reactivate(AccountStatus.Dormant));
        Assert.Throws<InvalidOperationException>(() => AccountLifecycle.Reactivate(AccountStatus.Closed));
    }

    [Fact]
    public void A_dormant_account_may_only_reach_closure_through_pending_closure()
    {
        Assert.Equal(AccountStatus.PendingClosure, AccountLifecycle.ProposeClosure(AccountStatus.Dormant));
        Assert.Equal(AccountStatus.Closed, AccountLifecycle.Close(AccountStatus.PendingClosure));
        Assert.Throws<InvalidOperationException>(() => AccountLifecycle.Close(AccountStatus.Dormant));
        Assert.Throws<InvalidOperationException>(() => AccountLifecycle.ProposeClosure(AccountStatus.Closed));
    }

    [Fact]
    public void A_closed_account_never_reopens()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AccountLifecycle.Reopen(AccountStatus.Closed));

        Assert.Equal(AccountLifecycle.ReopeningNotPermittedCode, exception.Message);
    }

    [Fact]
    public void The_dormancy_capabilities_use_the_three_segment_grammar()
    {
        Assert.Equal("account.dormancy.assess", AccountPermissions.DormancyAssess.Value);
        Assert.Equal("account.dormancy.reactivate", AccountPermissions.DormancyReactivate.Value);
        Assert.Equal(3, AccountPermissions.DormancyAssess.Value.Split('.').Length);
        Assert.Equal(3, AccountPermissions.DormancyReactivate.Value.Split('.').Length);
        Assert.True(AccountPermissions.DormancyReactivateRequirement.RequiresStepUp);
        Assert.False(AccountPermissions.DormancyAssessRequirement.RequiresStepUp);
    }

    [Fact]
    public void The_assessment_contract_exposes_every_recorded_fact()
    {
        DormancyAssessmentResponseV1 response = new(
            "institution-1",
            "account-1",
            "assessment-1",
            "2026-03-31",
            nameof(DormancyDecision.NoticeDue),
            TransitionPermitted: false,
            NoticeDue: true,
            [AccountDormancyPolicy.NoticeDueCode],
            "2026-01-01",
            89,
            90,
            "version-1",
            nameof(AccountStatus.Active),
            3,
            Replayed: false);

        Assert.Equal(89, response.InactivityDays);
        Assert.Equal(90, response.ThresholdDays);
        Assert.Contains(AccountDormancyPolicy.NoticeDueCode, response.ReasonCodes);
    }

    [Fact]
    public void The_activity_snapshot_reports_an_unavailable_source_rather_than_guessing()
    {
        AccountActivitySnapshot unavailable = new(
            [], "ledger:uninitialised", DateTimeOffset.UnixEpoch, AccountDormancyPolicy.ActivitySourceUnavailableCode);

        Assert.False(unavailable.Available);
        Assert.Empty(unavailable.Movements);
    }
}
