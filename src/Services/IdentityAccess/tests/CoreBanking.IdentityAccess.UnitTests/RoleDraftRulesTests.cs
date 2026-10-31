using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.IdentityAccess.Domain.Access;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

/// <summary>
/// Draft validation rules: stable codes, catalogue membership and self-conflict detection.
/// Decision: P1-E02-D09 and P1-E02-D10.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RoleDraftRulesTests
{
    [Theory]
    [InlineData("Product-Manager", "product-manager")]
    [InlineData("  auditor  ", "auditor")]
    [InlineData("ops.officer_1", "ops.officer_1")]
    public void A_valid_code_is_trimmed_and_lower_cased(string input, string expected) =>
        Assert.Equal(expected, RoleDraftRules.NormaliseCode(input));

    [Theory]
    [InlineData("1st-role")]
    [InlineData("a")]
    [InlineData("role with space")]
    [InlineData("role@bank")]
    [InlineData("")]
    public void An_invalid_code_is_refused(string input) =>
        Assert.Throws<ArgumentException>(() => RoleDraftRules.NormaliseCode(input));

    [Fact]
    public void Permissions_are_deduplicated_and_ordered() =>
        Assert.Equal(
            ["audit.event.view", "identity.role.create"],
            RoleDraftRules.NormalisePermissions(["identity.role.create", " audit.event.view ", "identity.role.create", ""]));

    [Fact]
    public void Unknown_permissions_are_reported_in_order()
    {
        HashSet<string> catalogue = new(StringComparer.Ordinal) { "audit.event.view" };

        IReadOnlyList<string> unknown = RoleDraftRules.UnknownPermissions(
            ["zeta.permission.one", "audit.event.view", "alpha.permission.two"],
            catalogue);

        Assert.Equal(["alpha.permission.two", "zeta.permission.one"], unknown);
    }

    [Fact]
    public void A_set_containing_both_sides_of_a_rule_conflicts_with_itself()
    {
        GrantConflict[] rules = [new("product.version.update", "product.version.publish")];

        IReadOnlyList<GrantConflict> conflicts = RoleDraftRules.SelfConflicts(
            ["product.version.update", "product.version.publish", "audit.event.view"],
            rules);

        Assert.Single(conflicts);
    }

    [Fact]
    public void A_set_outside_the_rules_is_clean()
    {
        GrantConflict[] rules = [new("product.version.update", "product.version.publish")];

        Assert.Empty(RoleDraftRules.SelfConflicts(["product.version.update", "audit.event.view"], rules));
    }
}
