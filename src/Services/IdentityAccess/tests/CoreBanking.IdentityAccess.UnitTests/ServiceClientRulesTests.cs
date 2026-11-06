using System;
using System.Linq;
using CoreBanking.IdentityAccess.Domain.Identities;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

/// <summary>
/// Service client rules: opaque references, future expiries and a fail-closed usability check.
/// Decision: ADR-0008; slice 13.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ServiceClientRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 13, 0, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedPermissions =
        ["service.audit.write", "service.identity.entitlements_read"];
    private static readonly string[] UnnormalisedPermissions =
        [" service.identity.entitlements_read ", "service.audit.write", "service.identity.entitlements_read"];

    [Fact]
    public void A_reference_is_trimmed() =>
        Assert.Equal("vault://identity/core-ledger", ServiceClientRules.NormaliseReference("  vault://identity/core-ledger "));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_reference_is_refused(string reference) =>
        Assert.Throws<ArgumentException>(() => ServiceClientRules.NormaliseReference(reference));

    [Fact]
    public void An_overlong_reference_is_refused() =>
        Assert.Throws<ArgumentException>(() => ServiceClientRules.NormaliseReference(new string('a', 201)));

    [Fact]
    public void An_empty_audience_is_refused() =>
        Assert.Throws<ArgumentException>(() => ServiceClientRules.NormaliseAudience(" "));

    [Fact]
    public void A_past_expiry_is_refused() =>
        Assert.Throws<ArgumentException>(() => ServiceClientRules.ValidateExpiry(Now.AddSeconds(-1), Now));

    [Fact]
    public void A_future_expiry_is_accepted() =>
        ServiceClientRules.ValidateExpiry(Now.AddDays(30), Now);

    [Fact]
    public void Service_permissions_are_trimmed_deduplicated_and_sorted()
    {
        string[] permissions = ServiceClientRules.NormalisePermissions(UnnormalisedPermissions).ToArray();

        Assert.Equal(ExpectedPermissions, permissions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("identity.identity.view")]
    [InlineData("service.identity")]
    [InlineData("service.identity.entitlements.read")]
    [InlineData("service.identity.entitlements-read")]
    public void Human_or_malformed_permissions_are_refused(string permission) =>
        Assert.Throws<ArgumentException>(() => ServiceClientRules.NormalisePermissions([permission]));

    [Fact]
    public void An_excessive_permission_set_is_refused() =>
        Assert.Throws<ArgumentException>(() => ServiceClientRules.NormalisePermissions(
            Enumerable.Range(0, ServiceClientRules.MaximumPermissions + 1)
                .Select(index => $"service.identity.action-{index}")));

    [Theory]
    [InlineData("Active", null, true)]
    [InlineData("Active", 30, true)]
    [InlineData("Active", -1, false)]
    [InlineData("Disabled", null, false)]
    [InlineData("active", null, false)]
    public void Usability_requires_an_active_unexpired_record(string status, int? expiryDays, bool expected)
    {
        DateTimeOffset? expiresAt = expiryDays is null ? null : Now.AddDays(expiryDays.Value);

        Assert.Equal(expected, ServiceClientRules.IsUsable(status, expiresAt, Now));
    }
}
