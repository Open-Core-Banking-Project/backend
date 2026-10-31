using System;

namespace CoreBanking.CustomerOnboarding.Domain.Parties;

/// <summary>
/// Date of birth as stored: encrypted, with a masked display form, the birth year kept readable so
/// age-band policy never needs the protected value, and a deterministic lookup token that exact
/// duplicate detection can compare without decrypting anything. Decision: CUS-028, P1-E03-D04-A.
/// </summary>
public sealed record ProtectedBirthDetails(
    string BirthDateToken,
    string BirthDateLookupToken,
    string BirthDateMasked,
    int BirthYear,
    string? CountryOfBirth,
    DateTimeOffset CapturedAt);
