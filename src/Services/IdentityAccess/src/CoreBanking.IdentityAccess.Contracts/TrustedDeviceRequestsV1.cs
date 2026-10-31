namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>
/// Starts a trusted-device registration. Only the public-key thumbprint and untrusted display metadata travel
/// in the request; no private key, secret, attestation claim or raw device signal.
/// Decision: P1-E02-D11.
/// </summary>
public sealed record StartDeviceRegistrationRequestV1(
    string KeyThumbprint,
    string Platform,
    string? DisplayName);
