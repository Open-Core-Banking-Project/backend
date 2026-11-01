using System;
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
