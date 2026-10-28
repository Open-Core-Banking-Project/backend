using System;

namespace CoreBanking.CustomerOnboarding.Domain.Parties;

/// <summary>
/// An address as stored: the free-form lines are encrypted into one token, a masked form is kept for
/// display and the structured parts stay readable for routing and policy checks. Decision: CUS-028.
/// </summary>
public sealed record ProtectedAddress(
    AddressType Type,
    string LineToken,
    string MaskedLine,
    string City,
    string? Region,
    string CountryCode,
    DateTimeOffset CapturedAt);
