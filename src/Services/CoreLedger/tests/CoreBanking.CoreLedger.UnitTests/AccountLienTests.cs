using System;
using System.Linq;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Domain.Accounts;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class AccountLienTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    private static AccountLien Lien(
        string id,
        LienPriorityCode code = LienPriorityCode.Customer,
        int rank = 0,
        decimal amount = 1_000m,
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveTo = null) =>
        AccountLienPolicy.Create(id, code, rank, amount, effectiveFrom ?? Now, effectiveTo);

    private static LedgerAvailabilitySnapshot Snapshot(decimal balance, decimal holds = 0m) =>
        new(balance, holds, balance - holds, "ledger:account-1:v1", Now);

    [Fact]
    public void Liens_order_by_priority_code_then_declared_rank_and_never_by_creation_order()
    {
        AccountLien customer = Lien("zzz-customer", LienPriorityCode.Customer, effectiveFrom: Now);
        AccountLien statutory = Lien("aaa-statutory", LienPriorityCode.StatutoryFirst, effectiveFrom: Now.AddDays(1));
        AccountLien taxLater = Lien("tax-second", LienPriorityCode.Tax, rank: 1, effectiveFrom: Now);
        AccountLien taxFirst = Lien("tax-first", LienPriorityCode.Tax, rank: 0, effectiveFrom: Now);

        AccountLien[] ordered = [.. AccountLienPolicy.Order([customer, taxLater, statutory, taxFirst])];

        Assert.Equal(["aaa-statutory", "tax-first", "tax-second", "zzz-customer"], ordered.Select(lien => lien.LienId));
    }

    [Fact]
    public void The_claimed_amount_is_immutable_and_a_partial_release_only_reduces_outstanding()
    {
        AccountLien lien = Lien("lien-1", amount: 1_000m);

        AccountLien released = AccountLienPolicy.PartialRelease(lien, 400m);

        Assert.Equal(1_000m, released.Amount);
        Assert.Equal(600m, released.OutstandingAmount);
        Assert.Equal(LienStatus.Active, released.Status);
        Assert.Equal(1_000m, lien.OutstandingAmount);
    }

    [Fact]
    public void A_release_that_consumes_the_outstanding_amount_closes_the_lien_and_keeps_the_claim()
    {
        AccountLien lien = Lien("lien-1", amount: 1_000m);

        AccountLien closed = AccountLienPolicy.PartialRelease(lien, 1_000m);

        Assert.Equal(LienStatus.Released, closed.Status);
        Assert.Equal(0m, closed.OutstandingAmount);
        Assert.Equal(1_000m, closed.Amount);
    }

    [Fact]
    public void A_full_release_closes_the_lien_whatever_remains_outstanding()
    {
        AccountLien lien = AccountLienPolicy.PartialRelease(Lien("lien-1"), 250m);

        AccountLien closed = AccountLienPolicy.FullRelease(lien);

        Assert.Equal(LienStatus.Released, closed.Status);
        Assert.Equal(0m, closed.OutstandingAmount);
        Assert.Equal(1_000m, closed.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000.01)]
    public void An_over_release_or_non_positive_release_is_refused(decimal amount)
    {
        AccountLien lien = Lien("lien-1", amount: 1_000m);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AccountLienPolicy.PartialRelease(lien, amount));

        Assert.Equal(AccountLienPolicy.OverReleaseCode, exception.Message);
    }

    [Fact]
    public void A_released_lien_cannot_be_released_again()
    {
        AccountLien closed = AccountLienPolicy.FullRelease(Lien("lien-1"));

        Assert.Throws<InvalidOperationException>(() => AccountLienPolicy.PartialRelease(closed, 1m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void A_lien_must_claim_a_positive_amount(decimal amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Lien("lien-1", amount: amount));
    }

    [Fact]
    public void A_lien_priority_rank_cannot_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Lien("lien-1", rank: -1));
    }

    [Fact]
    public void A_lien_effective_period_must_be_half_open_and_non_empty()
    {
        Assert.Throws<ArgumentException>(
            () => Lien("lien-1", effectiveFrom: Now, effectiveTo: Now));
    }

    [Theory]
    [InlineData("statutory_first", LienPriorityCode.StatutoryFirst, "statutory_first")]
    [InlineData(" TAX ", LienPriorityCode.Tax, "tax")]
    [InlineData("institution", LienPriorityCode.Institution, "institution")]
    [InlineData("customer", LienPriorityCode.Customer, "customer")]
    public void The_priority_code_allow_list_round_trips(string token, LienPriorityCode expected, string canonical)
    {
        LienPriorityCode parsed = AccountLien.ParseCode(token);

        Assert.Equal(expected, parsed);
        Assert.Equal(canonical, AccountLien.ToToken(parsed));
    }

    [Theory]
    [InlineData("legal")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unapproved_priority_code_is_refused(string? token)
    {
        Assert.Throws<ArgumentException>(() => AccountLien.ParseCode(token));
    }

    [Theory]
    [InlineData(LienPriorityCode.StatutoryFirst, true)]
    [InlineData(LienPriorityCode.Tax, true)]
    [InlineData(LienPriorityCode.Institution, true)]
    [InlineData(LienPriorityCode.Customer, false)]
    public void Statutory_tax_and_institution_liens_require_a_second_approver(LienPriorityCode code, bool expected)
    {
        Assert.Equal(expected, Lien("lien-1", code).RequiresSecondApprover);
    }

    [Fact]
    public void The_availability_position_reports_the_ledger_answer_and_only_the_shortfall_here()
    {
        LienAvailabilityPosition position = AccountLienPolicy.Combine(
            Snapshot(balance: 1_000m, holds: 200m),
            [Lien("lien-1", amount: 500m), Lien("lien-2", amount: 900m)]);

        Assert.Equal(1_400m, position.TotalOutstandingLiens);
        Assert.Equal(800m, position.ReportedAvailable);
        Assert.Equal(600m, position.UncoveredLienAmount);
        Assert.True(position.ExceedsAvailable);
        Assert.Equal("ledger:account-1:v1", position.SourceVersion);
        Assert.Equal(Now, position.AsOf);
    }

    [Fact]
    public void Liens_within_the_ledger_capacity_have_no_shortfall()
    {
        LienAvailabilityPosition position = AccountLienPolicy.Combine(
            Snapshot(balance: 1_000m),
            [Lien("lien-1", amount: 400m), Lien("lien-2", amount: 300m)]);

        Assert.Equal(700m, position.TotalOutstandingLiens);
        Assert.Equal(0m, position.UncoveredLienAmount);
        Assert.False(position.ExceedsAvailable);
    }

    [Fact]
    public void A_released_lien_leaves_the_outstanding_total()
    {
        AccountLien active = Lien("lien-1", amount: 400m);
        AccountLien closed = AccountLienPolicy.FullRelease(Lien("lien-2", amount: 900m));

        LienAvailabilityPosition position = AccountLienPolicy.Combine(
            Snapshot(balance: 1_000m),
            [active, closed]);

        Assert.Equal(400m, position.TotalOutstandingLiens);
        Assert.False(position.ExceedsAvailable);
    }

    [Fact]
    public void A_settlement_consumes_outstanding_amount_and_closes_at_zero()
    {
        AccountLien lien = Lien("lien-1", LienPriorityCode.Tax, amount: 500m);

        AccountLien partiallySettled = AccountLienPolicy.Satisfy(lien, 200m);
        AccountLien settled = AccountLienPolicy.Satisfy(partiallySettled, 300m);

        Assert.Equal(300m, partiallySettled.OutstandingAmount);
        Assert.Equal(LienStatus.Active, partiallySettled.Status);
        Assert.Equal(LienStatus.Released, settled.Status);
        Assert.Equal(500m, settled.Amount);
    }

    [Fact]
    public void A_lien_is_active_only_inside_its_effective_period()
    {
        AccountLien lien = Lien("lien-1", effectiveFrom: Now.AddDays(1), effectiveTo: Now.AddDays(3));

        Assert.False(lien.IsActiveAt(Now));
        Assert.True(lien.IsActiveAt(Now.AddDays(1)));
        Assert.True(lien.IsActiveAt(Now.AddDays(2)));
        Assert.False(lien.IsActiveAt(Now.AddDays(3)));
    }

    [Fact]
    public void Lien_permissions_follow_the_three_segment_grammar_and_need_step_up()
    {
        Assert.Equal("account.lien.create", AccountPermissions.LienCreate.Value);
        Assert.Equal("account.lien.release", AccountPermissions.LienRelease.Value);
        Assert.True(AccountPermissions.LienCreateRequirement.RequiresStepUp);
        Assert.True(AccountPermissions.LienReleaseRequirement.RequiresStepUp);
        Assert.True(AccountPermissions.LienReleaseRequirement.IsRestrictedApproval);
    }

    [Fact]
    public void The_lien_availability_contract_carries_no_derived_balance()
    {
        string[] properties = typeof(CoreBanking.CoreLedger.Contracts.LienAvailabilityPositionV1)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(
            [
                "TotalOutstandingLiens",
                "ReportedAvailable",
                "UncoveredLienAmount",
                "ExceedsAvailable",
                "SourceVersion",
                "AsOf",
            ],
            properties);
    }
}
