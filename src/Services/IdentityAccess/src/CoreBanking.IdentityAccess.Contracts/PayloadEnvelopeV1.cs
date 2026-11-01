using System;
using System.Collections.Generic;

namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>
/// The version 1 protected payload envelope as it travels on the wire. It carries one encrypted field, the
/// wrapping reference and the context the ciphertext is bound to. Decisions: P1-E02-D12, P1-E02-D13.
/// </summary>
public sealed record ProtectedPayloadEnvelopeV1
{
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Identifier of the institution wrapping key, from public-key discovery.</summary>
    public required string KeyId { get; init; }

    public required int KeyVersion { get; init; }

    /// <summary>Only <c>RSA-OAEP-256+A256GCM</c> is accepted.</summary>
    public required string Algorithm { get; init; }

    /// <summary>The server-side purpose the envelope was produced for. It cannot be chosen by the caller.</summary>
    public required string Purpose { get; init; }

    public required string Audience { get; init; }

    /// <summary>The allow-listed field this ciphertext carries.</summary>
    public required string Field { get; init; }

    /// <summary>Client issue time, in UTC, truncated to whole seconds for the authenticated context.</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>Client CSPRNG replay value, base64url, at least 128 bits. Single use.</summary>
    public required string ReplayId { get; init; }

    public required string WrappedKey { get; init; }

    public required string Nonce { get; init; }

    public required string Ciphertext { get; init; }

    public required string Tag { get; init; }
}

/// <summary>One advertised key version. Public parameters only; there is no private member to return.</summary>
public sealed record PublicKeyV1
{
    public required string KeyId { get; init; }

    public required int KeyVersion { get; init; }

    public required string Purpose { get; init; }

    public required string Algorithm { get; init; }

    /// <summary>The wrapping public key in PEM form.</summary>
    public required string PublicKey { get; init; }

    public required DateTimeOffset NotBefore { get; init; }

    public DateTimeOffset? NotAfter { get; init; }
}

/// <summary>The discovery response for one purpose. Active versions only. Decision: P1-E02-D13 item D13-F.</summary>
public sealed record PublicKeySetResponseV1
{
    public int SchemaVersion { get; init; } = 1;

    public required string Purpose { get; init; }

    public required IReadOnlyList<PublicKeyV1> Keys { get; init; }
}
