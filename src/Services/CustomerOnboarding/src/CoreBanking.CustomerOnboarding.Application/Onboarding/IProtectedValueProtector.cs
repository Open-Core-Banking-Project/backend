using System;

namespace CoreBanking.CustomerOnboarding.Application.Onboarding;

/// <summary>
/// One protected value: the encrypted token that is the stored form, the deterministic lookup token
/// used for exact-match detection, and the masked form shown to authorised staff.
/// </summary>
public sealed record ProtectedValue(string ValueToken, string LookupToken, string MaskedValue);

/// <summary>
/// Encryption and tokenisation port for personal data. Implementations must be deterministic only for
/// the lookup token and randomised for the value token, must never log plaintext, and must fail
/// closed when key material is missing. Decision: CUS-028.
/// </summary>
public interface IProtectedValueProtector
{
    ProtectedValue Protect(string plaintext);
}
