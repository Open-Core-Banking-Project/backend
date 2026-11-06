using System;

namespace CoreBanking.CustomerOnboarding.Application.Onboarding;

/// <summary>
/// A protected value could not be recovered. The message never contains plaintext, key material or
/// the token itself, so it is safe to surface as a stable code.
/// </summary>
public sealed class ProtectedValueException : Exception
{
    public ProtectedValueException(string code, string message) : base(message) => Code = code;

    public ProtectedValueException(string code, string message, Exception inner) : base(message, inner) => Code = code;

    public string Code { get; }
}
