using System.Collections.Generic;
using CoreBanking.CustomerOnboarding.Domain.Parties;

namespace CoreBanking.CustomerOnboarding.Domain.Onboarding;

/// <summary>
/// A protected capture as stored. Completeness is answered from the stored shape, so no code path
/// needs the plaintext to decide whether an application can be submitted. Decision: CUS-028.
/// </summary>
public sealed record OnboardingCaptureSnapshot(
    PersonName? LegalName,
    ProtectedBirthDetails? BirthDetails,
    IReadOnlyList<ProtectedContactPoint> Contacts,
    IReadOnlyList<ProtectedAddress> Addresses)
{
    /// <summary>The field groups still missing, empty when the snapshot can be submitted.</summary>
    public IReadOnlyList<string> MissingFields()
    {
        List<string> missing = [];
        if (LegalName is null)
        {
            missing.Add("legalName");
        }

        if (BirthDetails is null)
        {
            missing.Add("birthDate");
        }

        if (Contacts.Count == 0)
        {
            missing.Add("contacts");
        }

        if (Addresses.Count == 0)
        {
            missing.Add("addresses");
        }

        return missing;
    }

    public bool IsComplete => MissingFields().Count == 0;
}
