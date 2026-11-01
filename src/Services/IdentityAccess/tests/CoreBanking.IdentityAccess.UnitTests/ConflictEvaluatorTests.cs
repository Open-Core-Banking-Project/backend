using System;
using System.Collections.Generic;
using CoreBanking.IdentityAccess.Domain.Access;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

/// <summary>
/// Segregation-of-duties evaluation before a grant becomes effective. Rules are data, so the
/// evaluator must never assume a role name and must never treat an empty rule set as a wildcard.
/// Decision: ADR-0009 and P1-E02-D10.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ConflictEvaluatorTests
{
    private static readonly GrantConflict[] Rules =
    [
        new("product.version.update", "product.version.publish"),
        new("identity.permission.assign", "audit.event.view"),
    ];

    [Fact]
    public void A_prohibited_pair_is_detected_in_either_order()
    {
        HashSet<string> permissions = new(StringComparer.Ordinal) { "product.version.publish", "product.version.update" };

        GrantConflict? conflict = ConflictEvaluator.FindFirst(permissions, Rules);

        Assert.NotNull(conflict);
        Assert.Equal("product.version.update", conflict!.PermissionCode);
    }

    [Fact]
    public void A_rule_with_only_one_held_permission_is_not_a_conflict()
    {
        HashSet<string> permissions = new(StringComparer.Ordinal) { "product.version.update", "identity.identity.view" };

        Assert.Null(ConflictEvaluator.FindFirst(permissions, Rules));
    }

    [Fact]
    public void No_rules_means_no_conflict_but_also_no_permission()
    {
        HashSet<string> permissions = new(StringComparer.Ordinal) { "product.version.update", "product.version.publish" };

        Assert.Null(ConflictEvaluator.FindFirst(permissions, []));
    }

    [Fact]
    public void Comparison_is_case_sensitive_because_codes_are_permanent()
    {
        HashSet<string> permissions = new(StringComparer.Ordinal) { "Product.Version.Update", "Product.Version.Publish" };

        Assert.Null(ConflictEvaluator.FindFirst(permissions, Rules));
    }
}
