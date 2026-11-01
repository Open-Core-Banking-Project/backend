using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.IdentityAccess.Application.Security;
using CoreBanking.IdentityAccess.Contracts;

namespace CoreBanking.IdentityAccess.Api.Security;

/// <summary>Maps the wire envelope and discovery contracts onto the application types and back.</summary>
internal static class PayloadEnvelopeMapping
{
    public static ProtectedPayloadEnvelope ToApplication(ProtectedPayloadEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return new ProtectedPayloadEnvelope(
            envelope.SchemaVersion,
            envelope.KeyId ?? string.Empty,
            envelope.KeyVersion,
            envelope.Algorithm ?? string.Empty,
            envelope.Purpose ?? string.Empty,
            envelope.Audience ?? string.Empty,
            envelope.Field ?? string.Empty,
            envelope.IssuedAt,
            envelope.ReplayId ?? string.Empty,
            envelope.WrappedKey ?? string.Empty,
            envelope.Nonce ?? string.Empty,
            envelope.Ciphertext ?? string.Empty,
            envelope.Tag ?? string.Empty);
    }

    public static PublicKeySetResponseV1 ToResponse(PublicKeySetView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        List<PublicKeyV1> keys = view.Keys
            .Select(key => new PublicKeyV1
            {
                KeyId = key.KeyId,
                KeyVersion = key.KeyVersion,
                Purpose = key.Purpose,
                Algorithm = key.Algorithm,
                PublicKey = key.PublicKey,
                NotBefore = key.NotBefore,
                NotAfter = key.NotAfter,
            })
            .ToList();

        return new PublicKeySetResponseV1 { Purpose = view.Purpose, Keys = keys };
    }
}
