using System;

namespace CoreBanking.IdentityAccess.Domain.Authentication;

/// <summary>Factor families named by the approved decision P1-E02-D08.</summary>
public enum MfaFactorKind
{
    Totp = 1,
    Passkey = 2,
    Sms = 3,
    Push = 4,
}

public enum MfaEnrolmentStatus
{
    Pending = 0,
    Active = 1,
    Revoked = 2,
}

/// <summary>
/// The approved factor policy from decision P1-E02-D08. Only TOTP is available in the first
/// slice; passkeys, SMS and push remain named but unavailable until their own delivery work lands.
/// Decision: docs/implementation/phase-1-banking-foundation/decisions/P1-E02-D08-mfa-and-recovery-methods.md
/// </summary>
public static class MfaPolicy
{
    /// <summary>Step-up evidence lifetime approved for Phase 1.</summary>
    public static TimeSpan StepUpValidity { get; } = TimeSpan.FromMinutes(5);

    public static bool IsAvailable(MfaFactorKind kind) => kind == MfaFactorKind.Totp;
}

/// <summary>
/// A purpose-bound step-up grant. It is valid only for the actor, session, device, purpose and
/// instant that produced it, and can be consumed once.
/// </summary>
public static class StepUpEvidenceRules
{
    public static bool IsValidFor(
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? consumedAt,
        string purpose,
        string requestedPurpose,
        string sessionId,
        string requestedSessionId,
        string? deviceId,
        string? requestedDeviceId,
        DateTimeOffset now)
    {
        if (consumedAt is not null || now >= expiresAt || now < issuedAt)
        {
            return false;
        }

        if (!string.Equals(purpose, requestedPurpose, StringComparison.Ordinal)
            || !string.Equals(sessionId, requestedSessionId, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(deviceId ?? string.Empty, requestedDeviceId ?? string.Empty, StringComparison.Ordinal);
    }
}
