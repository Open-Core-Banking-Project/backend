using System;

namespace CoreBanking.CustomerOnboarding.Domain.Parties;

/// <summary>
/// Date of birth as stored: encrypted, with a masked display form and the birth year kept readable so
/// age-band policy never needs the protected value. Decision: CUS-028.
/// </summary>
public sealed record ProtectedBirthDetails(
    string BirthDateToken,
    string BirthDateMasked,
    int BirthYear,
    string? CountryOfBirth,
    DateTimeOffset CapturedAt);
