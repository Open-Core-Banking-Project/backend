using System;
using System.Linq;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Infrastructure.Protection;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Protection;

/// <summary>
/// The protection port is the only thing between captured personal data and the database, so the
/// properties that matter are asserted directly: randomised value tokens, deterministic lookup
/// tokens, masked display, and refusing to start without usable key material. Decision: CUS-028.
/// </summary>
public sealed class AesGcmProtectedValueProtectorTests
{
    [Fact]
    public void Two_captures_of_the_same_value_produce_different_value_tokens()
    {
        AesGcmProtectedValueProtector protector = CreateProtector();

        ProtectedValue first = protector.Protect("+234 801 234 5678");
        ProtectedValue second = protector.Protect("+234 801 234 5678");

        Assert.NotEqual(first.ValueToken, second.ValueToken);
        Assert.StartsWith("v1.", first.ValueToken, StringComparison.Ordinal);
        Assert.DoesNotContain("234", first.ValueToken, StringComparison.Ordinal);
    }

    [Fact]
    public void The_lookup_token_is_deterministic_and_does_not_contain_the_plaintext()
    {
        AesGcmProtectedValueProtector protector = CreateProtector();

        ProtectedValue first = protector.Protect("ada@example.com");
        ProtectedValue second = protector.Protect("  ada@example.com  ");

        Assert.Equal(first.LookupToken, second.LookupToken);
        Assert.Equal(64, first.LookupToken.Length);
        Assert.DoesNotContain("ada", first.LookupToken, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_masked_value_hides_the_body_and_keeps_the_tail()
    {
        AesGcmProtectedValueProtector protector = CreateProtector();

        ProtectedValue value = protector.Protect("+2348012345678");

        Assert.EndsWith("5678", value.MaskedValue, StringComparison.Ordinal);
        Assert.DoesNotContain("8012", value.MaskedValue, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_value_is_masked_completely()
    {
        AesGcmProtectedValueProtector protector = CreateProtector();

        Assert.Equal("***", protector.Protect("abc").MaskedValue);
    }

    [Fact]
    public void Different_key_material_produces_a_different_lookup_token()
    {
        ProtectedValue first = CreateProtector().Protect("ada@example.com");
        ProtectedValue second = CreateProtector(lookupSeed: 4).Protect("ada@example.com");

        Assert.NotEqual(first.LookupToken, second.LookupToken);
    }

    [Fact]
    public void A_missing_key_fails_closed() =>
        Assert.Throws<InvalidOperationException>(() => ProtectedValueProtectionOptions.Create(null, Base64(2)));

    [Fact]
    public void A_key_of_the_wrong_length_fails_closed() =>
        Assert.Throws<InvalidOperationException>(
            () => ProtectedValueProtectionOptions.Create(Convert.ToBase64String([1, 2, 3]), Base64(2)));

    [Fact]
    public void Reusing_one_secret_for_both_keys_fails_closed()
    {
        string key = Base64(3);

        Assert.Throws<InvalidOperationException>(() => ProtectedValueProtectionOptions.Create(key, key));
    }

    private static AesGcmProtectedValueProtector CreateProtector(int lookupSeed = 2) =>
        new(ProtectedValueProtectionOptions.Create(Base64(1), Base64(lookupSeed)));

    private static string Base64(int seed) =>
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)(seed * 32 + index)).ToArray());
}
