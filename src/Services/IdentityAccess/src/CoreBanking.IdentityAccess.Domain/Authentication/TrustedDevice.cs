using System;
using System.Linq;

namespace CoreBanking.IdentityAccess.Domain.Authentication;

/// <summary>Trust lifecycle from the epic. A revoked device never returns to trust. Decision: P1-E02-D11.</summary>
public enum DeviceTrustState
{
    PendingStepUp,
    Trusted,
    Suspended,
    Revoked,
}

/// <summary>The approved platform codes. A free-text platform string is never stored. Decision: P1-E02-D11.</summary>
public enum DevicePlatform
{
    Ios,
    Android,
    Web,
    Windows,
    Macos,
    Linux,
    Unknown,
}

public static class DeviceCodes
{
    public static string ToCode(DeviceTrustState state) => state switch
    {
        DeviceTrustState.PendingStepUp => "pending_step_up",
        DeviceTrustState.Trusted => "trusted",
        DeviceTrustState.Suspended => "suspended",
        DeviceTrustState.Revoked => "revoked",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static DeviceTrustState FromTrustCode(string code) => code switch
    {
        "pending_step_up" => DeviceTrustState.PendingStepUp,
        "trusted" => DeviceTrustState.Trusted,
        "suspended" => DeviceTrustState.Suspended,
        "revoked" => DeviceTrustState.Revoked,
        _ => throw new InvalidOperationException($"Unknown device trust code '{code}'."),
    };

    public static string ToCode(DevicePlatform platform) => platform switch
    {
        DevicePlatform.Ios => "ios",
        DevicePlatform.Android => "android",
        DevicePlatform.Web => "web",
        DevicePlatform.Windows => "windows",
        DevicePlatform.Macos => "macos",
        DevicePlatform.Linux => "linux",
        _ => "unknown",
    };

    public static DevicePlatform FromPlatformCode(string code) => code switch
    {
        "ios" => DevicePlatform.Ios,
        "android" => DevicePlatform.Android,
        "web" => DevicePlatform.Web,
        "windows" => DevicePlatform.Windows,
        "macos" => DevicePlatform.Macos,
        "linux" => DevicePlatform.Linux,
        _ => DevicePlatform.Unknown,
    };
}

/// <summary>
/// One trusted-device registration. Trust is effective-dated and step-up-gated, display metadata is untrusted
/// and no secret is stored: only a public-key thumbprint. Decision: P1-E02-D11.
/// </summary>
public sealed class TrustedDevice
{
    public const int MaximumDisplayNameLength = 64;
    public const int MaximumReasonLength = 500;
    public const string RegistrationPurpose = "identity.device.register";

    private TrustedDevice(
        string institutionId,
        string deviceId,
        string identityId,
        string keyThumbprint,
        DevicePlatform platform,
        string? displayName,
        DeviceTrustState state,
        DateTimeOffset createdAt,
        DateTimeOffset? trustedAt,
        DateTimeOffset? trustExpiresAt,
        DateTimeOffset? revokedAt,
        string? revocationReason,
        string? stepUpEvidenceId,
        long concurrencyVersion)
    {
        InstitutionId = institutionId;
        DeviceId = deviceId;
        IdentityId = identityId;
        KeyThumbprint = keyThumbprint;
        Platform = platform;
        DisplayName = displayName;
        State = state;
        CreatedAt = createdAt;
        TrustedAt = trustedAt;
        TrustExpiresAt = trustExpiresAt;
        RevokedAt = revokedAt;
        RevocationReason = revocationReason;
        StepUpEvidenceId = stepUpEvidenceId;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>Trusted tenant identity; the domain stays dependency-free, like the rest of this assembly.</summary>
    public string InstitutionId { get; }

    public string DeviceId { get; }

    public string IdentityId { get; }

    /// <summary>Base64url SHA-256 of the client's exported public key. Never a private key or a secret.</summary>
    public string KeyThumbprint { get; }

    public DevicePlatform Platform { get; }

    /// <summary>Untrusted, user-supplied label. Never used for authentication or policy selection.</summary>
    public string? DisplayName { get; }

    public DeviceTrustState State { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? TrustedAt { get; private set; }

    public DateTimeOffset? TrustExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevocationReason { get; private set; }

    public string? StepUpEvidenceId { get; private set; }

    public long ConcurrencyVersion { get; private set; }

    /// <summary>Effective trust: a revoked device is never trusted, and an expired registration behaves untrusted.</summary>
    public bool IsTrustedAt(DateTimeOffset now) =>
        State == DeviceTrustState.Trusted && TrustExpiresAt is not null && now < TrustExpiresAt.Value;

    public bool IsExpiredAt(DateTimeOffset now) =>
        State == DeviceTrustState.Trusted && TrustExpiresAt is not null && now >= TrustExpiresAt.Value;

    public static TrustedDevice StartRegistration(
        string institutionId,
        string identityId,
        string keyThumbprint,
        DevicePlatform platform,
        string? displayName,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityId);

        return new TrustedDevice(
            institutionId,
            Guid.NewGuid().ToString("N"),
            identityId.Trim(),
            Thumbprint(keyThumbprint),
            platform,
            Label(displayName),
            DeviceTrustState.PendingStepUp,
            now,
            trustedAt: null,
            trustExpiresAt: null,
            revokedAt: null,
            revocationReason: null,
            stepUpEvidenceId: null,
            concurrencyVersion: 0);
    }

    public static TrustedDevice Restore(
        string institutionId,
        string deviceId,
        string identityId,
        string keyThumbprint,
        DevicePlatform platform,
        string? displayName,
        DeviceTrustState state,
        DateTimeOffset createdAt,
        DateTimeOffset? trustedAt,
        DateTimeOffset? trustExpiresAt,
        DateTimeOffset? revokedAt,
        string? revocationReason,
        string? stepUpEvidenceId,
        long concurrencyVersion) =>
        new(
            institutionId,
            deviceId,
            identityId,
            keyThumbprint,
            platform,
            displayName,
            state,
            createdAt,
            trustedAt,
            trustExpiresAt,
            revokedAt,
            revocationReason,
            stepUpEvidenceId,
            concurrencyVersion);

    public void Activate(string stepUpEvidenceId, DateTimeOffset trustExpiresAt, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepUpEvidenceId);

        if (State == DeviceTrustState.Revoked)
        {
            throw new InvalidOperationException("device_revoked");
        }

        if (State is not (DeviceTrustState.PendingStepUp or DeviceTrustState.Suspended))
        {
            throw new InvalidOperationException("device_state_invalid");
        }

        State = DeviceTrustState.Trusted;
        TrustedAt = now;
        TrustExpiresAt = trustExpiresAt;
        StepUpEvidenceId = stepUpEvidenceId.Trim();
        ConcurrencyVersion++;
    }

    public void Suspend(DateTimeOffset now)
    {
        if (State != DeviceTrustState.Trusted)
        {
            throw new InvalidOperationException("device_state_invalid");
        }

        State = DeviceTrustState.Suspended;
        ConcurrencyVersion++;
    }

    public void Revoke(string reason, DateTimeOffset now)
    {
        string trimmed = (reason ?? string.Empty).Trim();
        if (trimmed.Length < 3 || trimmed.Length > MaximumReasonLength)
        {
            throw new InvalidOperationException("device_reason_required");
        }

        if (State == DeviceTrustState.Revoked)
        {
            throw new InvalidOperationException("device_revoked");
        }

        State = DeviceTrustState.Revoked;
        RevokedAt = now;
        RevocationReason = trimmed;
        ConcurrencyVersion++;
    }

    private static string Thumbprint(string keyThumbprint)
    {
        string trimmed = (keyThumbprint ?? string.Empty).Trim();
        bool valid = trimmed.Length is >= 43 and <= 64
            && trimmed.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

        return valid
            ? trimmed
            : throw new ArgumentException(
                "A device key thumbprint must be a base64url SHA-256 digest of the exported public key.",
                nameof(keyThumbprint));
    }

    private static string? Label(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        string trimmed = displayName.Trim();
        return trimmed.Length <= MaximumDisplayNameLength
            ? trimmed
            : throw new ArgumentException(
                $"A device display name may not exceed {MaximumDisplayNameLength} characters.",
                nameof(displayName));
    }
}
