using System;

namespace CoreBanking.IdentityAccess.Domain.Authentication;

/// <summary>Supported authentication channels. A policy is effective for one channel only.</summary>
public enum AuthenticationChannel
{
    Web,
    Mobile,
    StaffWeb,
    Agent,
    PartnerApi,
    Service,
}

/// <summary>Lifecycle for an immutable authentication policy version.</summary>
public enum AuthenticationPolicyStatus
{
    Draft,
    Active,
    Retired,
}

/// <summary>
/// Policy metadata only. Security floors and factor details remain centrally approved configuration;
/// this model deliberately does not invent password, MFA, or lockout thresholds.
/// </summary>
public sealed record AuthenticationPolicyVersion(
    string PolicyVersionId,
    AuthenticationChannel Channel,
    string Provider,
    AuthenticationPolicyStatus Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil)
{
    public bool IsEffectiveAt(DateTimeOffset instant) =>
        Status == AuthenticationPolicyStatus.Active &&
        EffectiveFrom <= instant &&
        (EffectiveUntil is null || instant < EffectiveUntil.Value);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PolicyVersionId))
        {
            throw new ArgumentException("A policy version identifier is required.", nameof(PolicyVersionId));
        }

        if (string.IsNullOrWhiteSpace(Provider))
        {
            throw new ArgumentException("An authentication provider is required.", nameof(Provider));
        }

        if (EffectiveUntil is not null && EffectiveUntil <= EffectiveFrom)
        {
            throw new ArgumentException("The policy effective end must be after its start.", nameof(EffectiveUntil));
        }
    }
}
