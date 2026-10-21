using System;
using CoreBanking.CoreLedger.Domain.Products;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class ProductEligibilityApprovalPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("current")]
    [InlineData("term_deposit")]
    [InlineData("wallet")]
    public void Only_savings_is_live_enabled(string family)
    {
        Assert.Throws<InvalidOperationException>(() => ProductFamilyPolicy.ParseLiveFamily(family));
    }

    [Fact]
    public void Savings_is_live_enabled()
    {
        Assert.Equal(ProductFamily.Savings, ProductFamilyPolicy.ParseLiveFamily("savings"));
    }

    [Fact]
    public void Expired_authoritative_facts_fail_closed_at_final_approval()
    {
        FinalEligibilityDecision decision = ProductEligibilityApprovalPolicy.RecheckForFinalApproval(
            Rules(), Facts() with { ValidUntil = Now.AddSeconds(-1) }, Now);

        Assert.False(decision.Eligible);
        Assert.False(decision.FactsCurrent);
        Assert.Equal("eligibility_facts_stale", decision.Code);
        Assert.Empty(decision.Trace);
    }

    [Fact]
    public void Current_facts_are_re_evaluated_and_their_exact_versions_are_retained()
    {
        AuthoritativeProductEligibilityFacts facts = Facts() with
        {
            CustomerFactsVersion = "customer-v8",
            KycTier = "KYC-1",
        };

        FinalEligibilityDecision decision = ProductEligibilityApprovalPolicy.RecheckForFinalApproval(Rules(), facts, Now);

        Assert.False(decision.Eligible);
        Assert.True(decision.FactsCurrent);
        Assert.Equal("product_eligibility_failed", decision.Code);
        Assert.Equal("customer-v8", decision.CustomerFactsVersion);
        Assert.Contains(decision.Trace, trace => trace.Rule == "kyc_tier" && !trace.Passed);
    }

    [Fact]
    public void Current_eligible_facts_can_pass_the_final_approval_gate()
    {
        FinalEligibilityDecision decision = ProductEligibilityApprovalPolicy.RecheckForFinalApproval(Rules(), Facts(), Now);

        Assert.True(decision.Eligible);
        Assert.True(decision.FactsCurrent);
        Assert.Equal("eligible", decision.Code);
        Assert.All(decision.Trace, trace => Assert.True(trace.Passed));
    }

    private static ProductEligibilityRules Rules() => ProductEligibilityRules.Create(
        "savings-eligibility", 1,
        EligibilityCodeCriterion.Only(["INDIVIDUAL"]),
        EligibilityCodeCriterion.Only(["KYC-2", "KYC-3"]),
        EligibilityCodeCriterion.Only(["RETAIL"]),
        EligibilityCodeCriterion.Only(["LOW", "MEDIUM"]),
        EligibilityAgeCriterion.Between(18, 65),
        EligibilityCodeCriterion.Only(["LAGOS-001"]),
        EligibilityCodeCriterion.Only(["NG"]),
        EligibilityCodeCriterion.Only(["NGN"]));

    private static AuthoritativeProductEligibilityFacts Facts() => new(
        "customer-v7",
        "branch-v2",
        "currency-v1",
        Now.AddMinutes(5),
        "INDIVIDUAL",
        "KYC-2",
        "RETAIL",
        "LOW",
        30,
        "LAGOS-001",
        "NG",
        "NGN");
}
