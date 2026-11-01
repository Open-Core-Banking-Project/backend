using System;

namespace CoreBanking.CustomerOnboarding.Domain;

/// <summary>
/// A rule the onboarding domain refuses to break. The code is stable and is what an API problem
/// detail reports; the message is for logs and tests. Decision: CUS-UC-01 failure codes.
/// </summary>
public sealed class OnboardingRuleException : Exception
{
    public OnboardingRuleException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
