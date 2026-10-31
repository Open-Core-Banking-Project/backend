using System;
using CoreBanking.IdentityAccess.Domain.Authentication;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

/// <summary>
/// Trusted-device rules: step-up-gated trust, effective-dated expiry, terminal revocation and untrusted display
/// metadata. Decision: P1-E02-D11.
/// </summary>
public sealed class TrustedDeviceRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 16, 0, 0, TimeSpan.Zero);

    private const string Thumbprint = "Zm9vYmFyYmF6cXV1eDEyMzQ1Njc4OTBhYmNkZWZnaGlqa2xtbm9wcXJzdHU";

    [Fact]
    public void A_new_registration_waits_for_step_up()
    {
        TrustedDevice device = Start();

        Assert.Equal(DeviceTrustState.PendingStepUp, device.State);
        Assert.Null(device.TrustExpiresAt);
        Assert.False(device.IsTrustedAt(Now));
        Assert.Null(device.DisplayName);
    }

    [Fact]
    public void Activation_records_the_evidence_and_the_trust_period()
    {
        TrustedDevice device = Start();

        device.Activate("evidence-1", Now.AddDays(30), Now);

        Assert.Equal(DeviceTrustState.Trusted, device.State);
        Assert.Equal("evidence-1", device.StepUpEvidenceId);
        Assert.Equal(Now.AddDays(30), device.TrustExpiresAt);
        Assert.True(device.IsTrustedAt(Now));
        Assert.True(device.IsTrustedAt(Now.AddDays(29)));
        Assert.False(device.IsTrustedAt(Now.AddDays(30)));
        Assert.True(device.IsExpiredAt(Now.AddDays(30)));
    }

    [Fact]
    public void A_revoked_device_never_becomes_trusted_again()
    {
        TrustedDevice device = Start();
        device.Activate("evidence-1", Now.AddDays(30), Now);
        device.Revoke("lost device", Now.AddDays(1));

        Assert.Equal(DeviceTrustState.Revoked, device.State);
        Assert.Equal("lost device", device.RevocationReason);
        Assert.False(device.IsTrustedAt(Now.AddDays(2)));

        Assert.Throws<InvalidOperationException>(() => device.Activate("evidence-2", Now.AddDays(60), Now.AddDays(2)));
        Assert.Throws<InvalidOperationException>(() => device.Revoke("again", Now.AddDays(3)));
    }

    [Fact]
    public void A_suspended_device_can_be_trusted_again_only_with_fresh_evidence()
    {
        TrustedDevice device = Start();
        device.Activate("evidence-1", Now.AddDays(30), Now);
        device.Suspend(Now.AddDays(1));

        Assert.Equal(DeviceTrustState.Suspended, device.State);
        Assert.False(device.IsTrustedAt(Now.AddDays(1)));

        device.Activate("evidence-2", Now.AddDays(31), Now.AddDays(1));

        Assert.Equal(DeviceTrustState.Trusted, device.State);
        Assert.Equal("evidence-2", device.StepUpEvidenceId);
    }

    [Fact]
    public void A_revocation_requires_a_reason()
    {
        TrustedDevice device = Start();
        device.Activate("evidence-1", Now.AddDays(30), Now);

        Assert.Throws<InvalidOperationException>(() => device.Revoke("  ", Now));
        Assert.Throws<InvalidOperationException>(() => device.Revoke("no", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has spaces in the thumbprint value which is invalid")]
    public void A_key_thumbprint_must_be_a_base64url_digest(string thumbprint)
    {
        Assert.Throws<ArgumentException>(() =>
            TrustedDevice.StartRegistration("inst-1", "identity-1", thumbprint, DevicePlatform.Ios, null, Now));
    }

    [Fact]
    public void A_display_name_is_trimmed_and_bounded()
    {
        TrustedDevice device = TrustedDevice.StartRegistration(
            "inst-1",
            "identity-1",
            Thumbprint,
            DevicePlatform.Android,
            "  Pixel 9  ",
            Now);

        Assert.Equal("Pixel 9", device.DisplayName);

        Assert.Throws<ArgumentException>(() => TrustedDevice.StartRegistration(
            "inst-1",
            "identity-1",
            Thumbprint,
            DevicePlatform.Android,
            new string('x', 65),
            Now));
    }

    [Fact]
    public void Platform_codes_round_trip_and_unknown_values_stay_unknown()
    {
        Assert.Equal("ios", DeviceCodes.ToCode(DevicePlatform.Ios));
        Assert.Equal(DevicePlatform.Linux, DeviceCodes.FromPlatformCode("linux"));
        Assert.Equal(DevicePlatform.Unknown, DeviceCodes.FromPlatformCode("symbian"));
        Assert.Equal("unknown", DeviceCodes.ToCode(DevicePlatform.Unknown));
    }

    private static TrustedDevice Start() =>
        TrustedDevice.StartRegistration("inst-1", "identity-1", Thumbprint, DevicePlatform.Web, null, Now);
}
