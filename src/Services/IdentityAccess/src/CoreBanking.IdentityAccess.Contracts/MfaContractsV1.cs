using System;

namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>Stable enrolment states and codes for the version 1 MFA contracts.</summary>
public static class MfaContractV1
{
    public const int SchemaVersion = IdentityAccessContractV1.SchemaVersion;
    public const string Totp = "totp";
    public const string Passkey = "passkey";
    public const string Sms = "sms";
    public const string Push = "push";

    public const string StatusPending = "pending";
    public const string StatusActive = "active";
    public const string StatusRevoked = "revoked";

    public const string StepUpVerified = "verified";
    public const string StepUpRejected = "rejected";

    public const string FactorNotAvailable = "mfa.factor_not_available";
    public const string FactorInvalid = "mfa.factor_invalid";
    public const string EnrolmentNotFound = "mfa.enrolment_not_found";
    public const string ProviderUnavailable = "mfa.provider_unavailable";
    public const string StepUpEvidenceRequired = "mfa.step_up_evidence_required";
}

/// <summary>Registers a second factor for the authenticated actor.</summary>
public sealed record MfaEnrolmentRequestV1
{
    public int SchemaVersion { get; init; } = MfaContractV1.SchemaVersion;

    /// <summary>One of <c>totp</c>, <c>passkey</c>, <c>sms</c> or <c>push</c>.</summary>
    public required string FactorKind { get; init; }

    /// <summary>
    /// An opaque provider reference for the factor, never a shared secret or private key.
    /// The provider keeps the secret material.
    /// </summary>
    public required string ProviderReference { get; init; }
}

public sealed record MfaEnrolmentResultV1
{
    public int SchemaVersion { get; init; } = MfaContractV1.SchemaVersion;

    public required string EnrolmentId { get; init; }

    public required string FactorKind { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Completes a step-up challenge for one purpose.</summary>
public sealed record StepUpChallengeRequestV1
{
    public int SchemaVersion { get; init; } = MfaContractV1.SchemaVersion;

    /// <summary>The protected operation the step-up is for, for example <c>identity.identity.create</c>.</summary>
    public required string Purpose { get; init; }

    /// <summary>
    /// The factor response in clear text. It is verified by the provider boundary and never persisted.
    /// Exactly one of <see cref="Response"/> and <see cref="ProtectedResponse"/> must be supplied.
    /// </summary>
    public string? Response { get; init; }

    /// <summary>
    /// The same factor response inside a protected payload envelope, so the value never leaves the client in clear
    /// text. Identity decrypts it through the KMS/HSM boundary. Decision: P1-E02-D12 item D12-H.
    /// </summary>
    public ProtectedPayloadEnvelopeV1? ProtectedResponse { get; init; }
}

public sealed record StepUpChallengeResultV1
{
    public int SchemaVersion { get; init; } = MfaContractV1.SchemaVersion;

    public required string Status { get; init; }

    public string? Code { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
}
