using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.IdentityAccess.Application.Security;

/// <summary>
/// The version 1 protected payload envelope. A client encrypts one allow-listed field with a fresh content key
/// and wraps that key with the institution's published wrapping key. Decisions: P1-E02-D12, P1-E02-D13.
/// </summary>
public sealed record ProtectedPayloadEnvelope(
    int SchemaVersion,
    string KeyId,
    int KeyVersion,
    string Algorithm,
    string Purpose,
    string Audience,
    string Field,
    DateTimeOffset IssuedAt,
    string ReplayId,
    string WrappedKey,
    string Nonce,
    string Ciphertext,
    string Tag);

/// <summary>The approved envelope parameters. Only values stated here are accepted. Decision: P1-E02-D12.</summary>
public static class PayloadEnvelopePolicy
{
    public const int SchemaVersion = 1;

    /// <summary>RSA-OAEP-SHA256 content-key wrap plus AES-256-GCM field encryption.</summary>
    public const string Algorithm = "RSA-OAEP-256+A256GCM";

    public const int ContentKeyBytes = 32;
    public const int NonceBytes = 12;
    public const int TagBytes = 16;
    public const int MaximumWrappedKeyBytes = 512;
    public const int MaximumCiphertextBytes = 4096;

    /// <summary>Accepted difference between client and server time for <c>issuedAt</c>.</summary>
    public static TimeSpan ClockSkew { get; } = TimeSpan.FromSeconds(120);

    /// <summary>How long a claimed <c>replayId</c> is remembered.</summary>
    public static TimeSpan ReplayWindow { get; } = TimeSpan.FromMinutes(15);

    /// <summary>How long a retired key version may still unwrap an in-flight envelope.</summary>
    public static TimeSpan RetirementOverlap { get; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Authenticated additional data. It binds the ciphertext to the purpose, audience, field, key version and
    /// issue time, so a ciphertext moved to another context fails authenticated decryption. The issue time is
    /// truncated to whole seconds so a client that omits sub-second precision still derives the same bytes.
    /// </summary>
    public static string AdditionalData(ProtectedPayloadEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return string.Join(
            '|',
            "v1",
            envelope.Purpose,
            envelope.Audience,
            envelope.Field,
            envelope.KeyId,
            envelope.KeyVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CanonicalIssuedAt(envelope.IssuedAt));
    }

    /// <summary>The issue time as whole UTC seconds, in ISO 8601 form.</summary>
    public static string CanonicalIssuedAt(DateTimeOffset issuedAt)
    {
        long ticks = issuedAt.UtcTicks - (issuedAt.UtcTicks % TimeSpan.TicksPerSecond);

        return new DateTimeOffset(ticks, TimeSpan.Zero)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>One server-side binding. A caller can never nominate its own purpose, audience or field.</summary>
public sealed record PayloadFieldBinding(string Purpose, string Audience, string Field);

/// <summary>The allow-listed protected fields. Phase 1 protects the step-up factor response only.</summary>
public static class PayloadFieldBindings
{
    public static PayloadFieldBinding StepUpResponse { get; } =
        new("identity.step_up.response", "identity-access", "response");
}

/// <summary>The four stable failure codes the epic names. Nothing else reaches a caller.</summary>
public static class PayloadEnvelopeCodes
{
    public const string Invalid = "payload_encryption_invalid";
    public const string KeyExpired = "payload_key_expired";
    public const string KeyRevoked = "payload_key_revoked";
    public const string DecryptionUnavailable = "payload_decryption_unavailable";
}

/// <summary>A refusal that is safe to report. The message never contains key material, ciphertext or plaintext.</summary>
public sealed class PayloadEnvelopeException : Exception
{
    public PayloadEnvelopeException(string code, string message)
        : base(message) => Code = code;

    public PayloadEnvelopeException(string code, string message, Exception? innerException)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

/// <summary>
/// One unprotect request. The audit context is validated by the service and used only to record the attempt; it
/// never reaches the KMS/HSM adapter.
/// </summary>
public sealed record PayloadEnvelopeRequest(
    InstitutionId InstitutionId,
    string IdentityId,
    string? SessionId,
    string CorrelationId,
    ProtectedPayloadEnvelope Envelope);

/// <summary>
/// Validates one envelope against its server-side binding, claims its replay identifier, unwraps the content key
/// through the KMS/HSM port and returns the field plaintext for one operation.
/// Decision: P1-E02-D12 (format and replay), P1-E02-D13 (key boundary).
/// </summary>
public interface IPayloadEnvelopeService
{
    Task<byte[]> UnprotectAsync(
        PayloadEnvelopeRequest request,
        PayloadFieldBinding binding,
        CancellationToken cancellationToken);
}
