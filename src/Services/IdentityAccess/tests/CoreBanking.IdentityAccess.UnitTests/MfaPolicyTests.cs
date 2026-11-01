using System;
using CoreBanking.IdentityAccess.Domain.Authentication;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

/// <summary>
/// The approved factor policy from P1-E02-D08: only TOTP is available in the first slice, and
/// step-up evidence is valid only for its actor, session, device, purpose and five-minute window.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MfaPolicyTests
{
    private const string Purpose = "identity.identity.create";
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(MfaFactorKind.Passkey, false)]
    [InlineData(MfaFactorKind.Sms, false)]
    [InlineData(MfaFactorKind.Push, false)]
    [InlineData(MfaFactorKind.Totp, true)]
    public void Only_totp_is_available_in_the_first_slice(MfaFactorKind kind, bool expected) =>
        Assert.Equal(expected, MfaPolicy.IsAvailable(kind));

    [Fact]
    public void Step_up_evidence_lasts_five_minutes() =>
        Assert.Equal(TimeSpan.FromMinutes(5), MfaPolicy.StepUpValidity);

    [Fact]
    public void Matching_purpose_session_and_device_is_valid() =>
        Assert.True(IsValid(Now, Purpose, "session-1", "device-1", Purpose, "session-1", "device-1", Now));

    [Fact]
    public void A_different_purpose_is_invalid() =>
        Assert.False(IsValid(Now, Purpose, "session-1", "device-1", "identity.identity.update", "session-1", "device-1", Now));

    [Fact]
    public void A_different_session_is_invalid() =>
        Assert.False(IsValid(Now, Purpose, "session-1", "device-1", Purpose, "session-2", "device-1", Now));

    [Fact]
    public void A_different_device_is_invalid() =>
        Assert.False(IsValid(Now, Purpose, "session-1", "device-1", Purpose, "session-1", "device-2", Now));

    [Fact]
    public void Expired_evidence_is_invalid() =>
        Assert.False(IsValid(Now, Purpose, "session-1", "device-1", Purpose, "session-1", "device-1", Now.AddMinutes(5)));

    [Fact]
    public void Consumed_evidence_is_invalid() =>
        Assert.False(StepUpEvidenceRules.IsValidFor(
            Now, Now.AddMinutes(5), Now, Purpose, Purpose, "session-1", "session-1", "device-1", "device-1", Now));

    [Fact]
    public void Evidence_issued_in_the_future_is_invalid() =>
        Assert.False(StepUpEvidenceRules.IsValidFor(
            Now.AddMinutes(1), Now.AddMinutes(6), null, Purpose, Purpose, "session-1", "session-1", "device-1", "device-1", Now));

    private static bool IsValid(
        DateTimeOffset issuedAt,
        string purpose,
        string sessionId,
        string? deviceId,
        string requestedPurpose,
        string requestedSessionId,
        string? requestedDeviceId,
        DateTimeOffset now) =>
        StepUpEvidenceRules.IsValidFor(
            issuedAt,
            issuedAt.Add(MfaPolicy.StepUpValidity),
            null,
            purpose,
            requestedPurpose,
            sessionId,
            requestedSessionId,
            deviceId,
            requestedDeviceId,
            now);
}
