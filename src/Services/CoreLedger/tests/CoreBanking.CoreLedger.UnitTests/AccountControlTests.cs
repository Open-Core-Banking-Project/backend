using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Domain.Accounts;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class AccountControlPrecedenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    private static AccountControlDescriptor Control(
        string id,
        AccountControlType type,
        AccountControlDirection direction = AccountControlDirection.Both,
        string[]? channels = null,
        string[]? transactionTypes = null,
        string status = "active",
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveTo = null,
        bool requiresManualRelease = false) =>
        new(id, type, status, direction, channels ?? [], transactionTypes ?? [], effectiveFrom ?? Now.AddDays(-1), effectiveTo, requiresManualRelease);

    private static AccountControlDecision Evaluate(
        IReadOnlyCollection<AccountControlDescriptor> controls,
        string direction = "debit",
        string channel = "mobile",
        long knownVersion = 1,
        long currentVersion = 1) =>
        AccountControlPrecedencePolicy.Evaluate(
            controls,
            new AccountControlIntent(direction, "transfer", channel, Now),
            currentVersion,
            knownVersion);

    [Fact]
    public void An_account_without_controls_allows_the_intent()
    {
        AccountControlDecision decision = Evaluate([]);

        Assert.Equal(AccountControlOutcomes.Allow, decision.Outcome);
        Assert.True(decision.Allowed);
        Assert.Empty(decision.ReasonCodes);
    }

    [Fact]
    public void One_prohibitive_control_denies_regardless_of_any_other_control()
    {
        AccountControlDecision decision = Evaluate(
        [
            Control("freeze-1", AccountControlType.InstitutionFreeze),
            Control("restriction-1", AccountControlType.TypedRestriction, AccountControlDirection.Credit),
            Control("notice-1", AccountControlType.Notice),
        ]);

        Assert.Equal(AccountControlOutcomes.Deny, decision.Outcome);
        Assert.Single(decision.DenyingControls);
        Assert.Contains("control_denied:institution_freeze", decision.ReasonCodes);
    }

    [Fact]
    public void Reason_codes_are_ordered_deduplicated_and_name_every_denying_type()
    {
        AccountControlDecision decision = Evaluate(
        [
            Control("restriction-b", AccountControlType.TypedRestriction),
            Control("restriction-a", AccountControlType.TypedRestriction),
            Control("legal-1", AccountControlType.LegalBlock),
        ]);

        Assert.Equal(["control_denied:legal_block", "control_denied:typed_restriction"], decision.ReasonCodes);
        Assert.Equal(3, decision.DenyingControls.Count);
    }

    [Theory]
    [InlineData("debit", AccountControlOutcomes.Deny)]
    [InlineData("credit", AccountControlOutcomes.Allow)]
    public void An_institution_freeze_denies_debits_and_admits_credits(string direction, string expected)
    {
        AccountControlDecision decision = Evaluate(
            [Control("freeze-1", AccountControlType.InstitutionFreeze)],
            direction);

        Assert.Equal(expected, decision.Outcome);
    }

    [Theory]
    [InlineData("debit")]
    [InlineData("credit")]
    public void A_regulatory_freeze_denies_both_directions(string direction)
    {
        AccountControlDecision decision = Evaluate(
            [Control("freeze-1", AccountControlType.RegulatoryFreeze)],
            direction);

        Assert.Equal(AccountControlOutcomes.Deny, decision.Outcome);
    }

    [Fact]
    public void A_typed_restriction_never_denies_outside_its_declared_scope()
    {
        AccountControlDescriptor restriction = Control(
            "restriction-1",
            AccountControlType.TypedRestriction,
            AccountControlDirection.Debit,
            channels: ["branch"],
            transactionTypes: ["cash_withdrawal"]);

        Assert.Equal(AccountControlOutcomes.Allow, Evaluate([restriction], "debit", "mobile").Outcome);
        Assert.Equal(AccountControlOutcomes.Allow, Evaluate([restriction], "debit", "branch").Outcome);
        Assert.Equal(
            AccountControlOutcomes.Deny,
            AccountControlPrecedencePolicy.Evaluate(
                [restriction],
                new AccountControlIntent("debit", "cash_withdrawal", "branch", Now),
                1,
                1).Outcome);
        Assert.Equal(AccountControlOutcomes.Allow, Evaluate([restriction], "credit", "branch").Outcome);
    }

    [Fact]
    public void A_lien_and_a_notice_never_deny()
    {
        AccountControlDecision decision = Evaluate(
        [
            Control("lien-1", AccountControlType.Lien),
            Control("notice-1", AccountControlType.Notice),
        ]);

        Assert.Equal(AccountControlOutcomes.Allow, decision.Outcome);
        Assert.Empty(decision.DenyingControls);
        Assert.Equal(2, decision.EffectiveControls.Count);
    }

    [Fact]
    public void A_control_is_effective_from_its_start_and_not_at_its_end()
    {
        AccountControlDescriptor control = Control(
            "restriction-1",
            AccountControlType.TypedRestriction,
            effectiveFrom: Now,
            effectiveTo: Now.AddHours(1));

        long before = AccountControlPrecedencePolicy.Evaluate(
            [control], new AccountControlIntent("debit", "transfer", "mobile", Now.AddSeconds(-1)), 1, 1).DenyingControls.Count;
        long atStart = AccountControlPrecedencePolicy.Evaluate(
            [control], new AccountControlIntent("debit", "transfer", "mobile", Now), 1, 1).DenyingControls.Count;
        long atEnd = AccountControlPrecedencePolicy.Evaluate(
            [control], new AccountControlIntent("debit", "transfer", "mobile", Now.AddHours(1)), 1, 1).DenyingControls.Count;

        Assert.Equal(0, before);
        Assert.Equal(1, atStart);
        Assert.Equal(0, atEnd);
    }

    [Fact]
    public void A_released_or_inactive_control_does_not_apply()
    {
        AccountControlDecision decision = Evaluate(
            [Control("restriction-1", AccountControlType.TypedRestriction, status: "released")]);

        Assert.Equal(AccountControlOutcomes.Allow, decision.Outcome);
        Assert.Empty(decision.EffectiveControls);
    }

    [Fact]
    public void A_stale_control_version_fails_closed_into_review()
    {
        AccountControlDecision decision = Evaluate(
            [Control("restriction-1", AccountControlType.TypedRestriction)],
            knownVersion: 3,
            currentVersion: 4);

        Assert.Equal(AccountControlOutcomes.Review, decision.Outcome);
        Assert.Equal(AccountControlOutcomes.VersionStaleCode, decision.ReasonCodes[0]);
    }

    [Fact]
    public void A_manually_releasable_prohibition_yields_review_and_an_unconditional_one_denies()
    {
        AccountControlDecision releasable = Evaluate(
            [Control("block-1", AccountControlType.LegalBlock, requiresManualRelease: true)]);
        AccountControlDecision unconditional = Evaluate(
        [
            Control("block-1", AccountControlType.LegalBlock, requiresManualRelease: true),
            Control("freeze-1", AccountControlType.InstitutionFreeze),
        ]);

        Assert.Equal(AccountControlOutcomes.Review, releasable.Outcome);
        Assert.Equal(AccountControlOutcomes.Deny, unconditional.Outcome);
    }

    [Fact]
    public void The_same_control_set_always_produces_the_same_decision()
    {
        AccountControlDescriptor[] controls =
        [
            Control("b", AccountControlType.TypedRestriction),
            Control("a", AccountControlType.LegalBlock),
            Control("c", AccountControlType.InstitutionFreeze),
        ];

        AccountControlDecision first = Evaluate(controls);
        AccountControlDecision second = Evaluate(controls.Reverse().ToArray());

        Assert.Equal(first.Outcome, second.Outcome);
        Assert.Equal(first.ReasonCodes, second.ReasonCodes);
        Assert.Equal(
            first.DenyingControls.Select(control => control.ControlId),
            second.DenyingControls.Select(control => control.ControlId));
    }

    [Theory]
    [InlineData("debit")]
    [InlineData("credit")]
    [InlineData("both")]
    public void The_direction_vocabulary_is_allow_listed(string direction)
    {
        Assert.Equal(direction, AccountControlDescriptor.ToToken(AccountControlDescriptor.ParseDirection(direction)));
    }

    [Fact]
    public void An_unknown_direction_is_refused()
    {
        Assert.Throws<ArgumentException>(() => AccountControlDescriptor.ParseDirection("sideways"));
    }

    [Fact]
    public void Control_permissions_follow_the_three_segment_grammar()
    {
        Assert.Equal("account.restriction.apply", AccountPermissions.RestrictionApply.Value);
        Assert.Equal("account.restriction.release", AccountPermissions.RestrictionRelease.Value);
        Assert.Equal("account.controls.evaluate", AccountPermissions.ControlsEvaluate.Value);
        Assert.True(AccountPermissions.RestrictionApplyRequirement.RequiresStepUp);
        Assert.True(AccountPermissions.RestrictionReleaseRequirement.RequiresStepUp);
    }

    [Fact]
    public void The_control_decision_contract_carries_no_monetary_field()
    {
        string[] properties = typeof(CoreBanking.CoreLedger.Contracts.AccountControlDecisionV1)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Balance", properties);
        Assert.Contains("Outcome", properties);
        Assert.Contains("ReasonCodes", properties);
    }
}
