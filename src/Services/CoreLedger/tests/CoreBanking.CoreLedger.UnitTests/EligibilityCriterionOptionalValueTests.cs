using System;
using CoreBanking.CoreLedger.Domain.Products;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

/// <summary>
/// The residency fact is optional: an unrestricted product rule must evaluate, and a restrictive one
/// must fail closed. Without the optional form the evaluator threw for every published product, which
/// no account application could survive. Decisions: P1-E04-D17, P1-E04-D07.
/// </summary>
public sealed class EligibilityCriterionOptionalValueTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_absent_optional_fact_satisfies_an_unrestricted_rule()
    {
        EligibilityCodeCriterion criterion = EligibilityCodeCriterion.Any();

        Assert.True(criterion.AllowsOptional(null));
        Assert.True(criterion.AllowsOptional(string.Empty));
    }

    [Fact]
    public void An_absent_optional_fact_never_satisfies_an_allow_list()
    {
        EligibilityCodeCriterion criterion = EligibilityCodeCriterion.Only(["NG"]);

        Assert.False(criterion.AllowsOptional(null));
        Assert.False(criterion.AllowsOptional(string.Empty));
        Assert.True(criterion.AllowsOptional("NG"));
    }

    [Fact]
    public void A_final_recheck_without_a_residency_fact_is_eligible_for_an_unrestricted_product()
    {
        FinalEligibilityDecision decision = ProductEligibilityApprovalPolicy.RecheckForFinalApproval(
            Rules(),
            Facts(residencyCountryCode: null),
            EvaluatedAt);

        Assert.True(decision.Eligible);
        Assert.Equal("eligible", decision.Code);
        Assert.Contains(decision.Trace, rule => rule.Rule == "residency_country" && rule.Passed);
    }

    [Fact]
    public void A_final_recheck_without_a_required_residency_fact_fails_closed()
    {
        FinalEligibilityDecision decision = ProductEligibilityApprovalPolicy.RecheckForFinalApproval(
            Rules(EligibilityCodeCriterion.Only(["NG"])),
            Facts(residencyCountryCode: null),
            EvaluatedAt);

        Assert.False(decision.Eligible);
        Assert.Equal("residency_fact_unavailable", decision.Code);
        Assert.Empty(decision.Trace);
    }

    private static ProductEligibilityRules Rules(EligibilityCodeCriterion? residencyCountries = null) =>
        ProductEligibilityRules.Create(
            "savings-standard",
            1,
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityAgeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            residencyCountries ?? EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any());

    private static AuthoritativeProductEligibilityFacts Facts(string? residencyCountryCode) =>
        new(
            "customer-facts-1",
            "branch-reference-1",
            "currency-reference-1",
            EvaluatedAt.AddSeconds(30),
            "Individual",
            "tier-1",
            "Retail",
            "Low",
            34,
            "BR-001",
            residencyCountryCode,
            "NGN");
}
