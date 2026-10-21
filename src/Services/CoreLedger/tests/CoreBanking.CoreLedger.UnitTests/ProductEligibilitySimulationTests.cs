using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Products;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class ProductEligibilitySimulationTests
{
    [Fact]
    public void Explicit_rules_return_a_trace_for_every_eligibility_dimension()
    {
        ProductEligibilitySimulationResult result = ProductEligibilitySimulator.Simulate(
            "simulation-1", Rules(), [Scenario("eligible", age: 18, expected: true)], At);

        ProductEligibilityScenarioResult scenario = Assert.Single(result.Scenarios);
        Assert.True(result.Passed);
        Assert.True(scenario.Eligible);
        Assert.Equal(8, scenario.Trace.Count);
        Assert.All(scenario.Trace, step => Assert.True(step.Passed));
        Assert.Equal(64, result.InputHash.Length);
        Assert.Equal(64, result.OutputHash.Length);
    }

    [Fact]
    public void Age_boundaries_are_inclusive_and_outside_values_fail_closed()
    {
        ProductEligibilitySimulationResult result = ProductEligibilitySimulator.Simulate(
            "simulation-1",
            Rules(),
            [
                Scenario("minimum", age: 18, expected: true),
                Scenario("maximum", age: 65, expected: true),
                Scenario("below", age: 17, expected: false),
                Scenario("above", age: 66, expected: false),
            ],
            At);

        Assert.True(result.Passed);
        Assert.All(result.Scenarios, scenario => Assert.Equal(scenario.ExpectedEligible, scenario.Eligible));
    }

    [Fact]
    public void A_disallowed_authoritative_fact_makes_the_scenario_ineligible()
    {
        ProductEligibilitySimulationResult result = ProductEligibilitySimulator.Simulate(
            "simulation-1", Rules(), [Scenario("wrong-kyc", age: 30, expected: false) with { KycTier = "KYC-1" }], At);

        ProductEligibilityScenarioResult scenario = Assert.Single(result.Scenarios);
        Assert.True(result.Passed);
        Assert.False(scenario.Eligible);
        Assert.False(scenario.Trace.Single(x => x.Rule == "kyc_tier").Passed);
    }

    [Fact]
    public void Missing_fact_or_mixed_dataset_versions_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ProductEligibilitySimulator.Simulate(
            "simulation-1", Rules(), [Scenario("missing", 30, true) with { Segment = "" }], At));

        Assert.Throws<ArgumentException>(() => ProductEligibilitySimulator.Simulate(
            "simulation-1", Rules(),
            [Scenario("one", 30, true), Scenario("two", 30, true) with { DatasetVersion = "dataset-v2" }], At));
    }

    [Fact]
    public void Failed_expected_outcome_cannot_unlock_configuration_submission()
    {
        ProductEligibilityRules rules = Rules();
        ProductEligibilitySimulationResult result = ProductEligibilitySimulator.Simulate(
            "simulation-1", rules, [Scenario("incorrect-expectation", age: 17, expected: true)], At);
        InstitutionConfigurationVersion version = InstitutionConfigurationVersion.Create(
            InstitutionId.From("institution-1"), "product-eligibility", "version-1", 1,
            "product_eligibility", "product:SAVINGS-001", rules.ToConfigurationContent(), ActorId.From("maker-1"));

        version.RecordSimulation(result.ToConfigurationEvidence());

        Assert.False(result.Passed);
        Assert.Throws<InvalidOperationException>(() => version.Submit(ActorId.From("maker-1")));
    }

    [Fact]
    public void Passed_exact_hash_simulation_unlocks_the_controlled_lifecycle()
    {
        ProductEligibilityRules rules = Rules();
        ProductEligibilitySimulationResult result = ProductEligibilitySimulator.Simulate(
            "simulation-1", rules,
            [Scenario("eligible", 30, true), Scenario("below-minimum-age", 17, false)], At);
        InstitutionConfigurationVersion version = InstitutionConfigurationVersion.Create(
            InstitutionId.From("institution-1"), "product-eligibility", "version-1", 1,
            "product_eligibility", "product:SAVINGS-001", rules.ToConfigurationContent(), ActorId.From("maker-1"));

        version.RecordSimulation(result.ToConfigurationEvidence());
        version.Submit(ActorId.From("maker-1"));
        version.Approve(ActorId.From("approver-1"), "approval-1");
        version.Publish(
            ActorId.From("publisher-1"),
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            []);

        Assert.True(result.Passed);
        Assert.Equal(ConfigurationVersionStatus.Published, version.Status);
        Assert.Equal(result.ContentHash, version.Content.Hash);
    }

    [Fact]
    public void Equivalent_rules_and_scenarios_produce_stable_hashes()
    {
        ProductEligibilitySimulationResult first = ProductEligibilitySimulator.Simulate(
            "simulation-1", Rules(), [Scenario("eligible", 30, true)], At);
        ProductEligibilitySimulationResult second = ProductEligibilitySimulator.Simulate(
            "simulation-2", Rules(), [Scenario("eligible", 30, true)], At.AddHours(1));

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(first.InputHash, second.InputHash);
        Assert.Equal(first.OutputHash, second.OutputHash);
    }

    [Fact]
    public void Persisted_typed_rules_round_trip_to_the_same_canonical_hash()
    {
        ConfigurationContent content = Rules().ToConfigurationContent();

        ProductEligibilityRules restored = ProductEligibilityRules.FromConfigurationContent(content);

        Assert.Equal(content.Hash, restored.ToConfigurationContent().Hash);
        Assert.Equal("savings-eligibility", restored.RuleSetId);
        Assert.True(restored.Currencies.Allows("NGN"));
    }

    [Fact]
    public void Persisted_rules_with_unknown_fields_are_rejected()
    {
        ConfigurationContent valid = Rules().ToConfigurationContent();
        ConfigurationContent extended = ConfigurationContent.Create(
            "{\"unsupportedExpression\":\"customer => true\"," + valid.CanonicalJson[1..]);

        Assert.Throws<ArgumentException>(() => ProductEligibilityRules.FromConfigurationContent(extended));
    }

    [Fact]
    public void Product_codes_are_stable_institution_defined_identifiers()
    {
        ProductCode code = ProductCode.From(" savings.retail-01 ");
        InstitutionProduct first = InstitutionProduct.Create(
            InstitutionId.From("institution-1"), code, ProductFamily.Savings);
        InstitutionProduct second = InstitutionProduct.Create(
            InstitutionId.From("institution-2"), code, ProductFamily.Savings);

        Assert.Equal("SAVINGS.RETAIL-01", code.Value);
        Assert.Equal(code, first.Code);
        Assert.Equal(code, second.Code);
        Assert.NotEqual(first.InstitutionId, second.InstitutionId);
        Assert.Throws<ArgumentException>(() => ProductCode.From("savings retail"));
    }

    private static readonly DateTimeOffset At = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

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

    private static ProductEligibilitySimulationInput Scenario(string id, int age, bool expected) => new(
        id,
        "dataset-v1",
        "customer-facts-v4",
        "branch-reference-v2",
        "currency-reference-v1",
        "INDIVIDUAL",
        "KYC-2",
        "RETAIL",
        "LOW",
        age,
        "LAGOS-001",
        "NG",
        "NGN",
        expected);
}
