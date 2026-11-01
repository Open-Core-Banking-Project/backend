using System;
using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CustomerOnboarding.Domain.Parties;

/// <summary>Party kinds. Phase 1 captures individuals only; organisations are Phase 2 scope.</summary>
public enum PartyType
{
    Individual,
}

/// <summary>Party lifecycle. A party is not a customer until the institution relationship exists.</summary>
public enum PartyStatus
{
    Pending,
    Active,
    Restricted,
    Merged,
    Closed,
}

/// <summary>
/// A protected contact point as stored: the value is tokenised, a masked form is kept for display
/// and a deterministic lookup token supports duplicate detection without plaintext.
/// Decision: CUS-028, CUS-UC-01.
/// </summary>
public sealed record ProtectedContactPoint(
    ContactPointType Type,
    string ValueToken,
    string MaskedValue,
    string LookupToken,
    DateTimeOffset CapturedAt);

/// <summary>
/// A stable person identity owned by Customer and Onboarding. Created when an onboarding
/// application is submitted, and reused by later institution relationships; it is not itself a
/// customer. Decision: invariants 1 and 2.
/// </summary>
public sealed class Party
{
    private readonly List<ProtectedContactPoint> _contacts;
    private readonly List<ProtectedAddress> _addresses;

    private Party(
        InstitutionId institutionId,
        string partyId,
        PartyType type,
        PartyStatus status,
        PersonName legalName,
        ProtectedBirthDetails birthDetails,
        IReadOnlyList<ProtectedContactPoint> contacts,
        IReadOnlyList<ProtectedAddress> addresses,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        long concurrencyVersion)
    {
        InstitutionId = institutionId;
        PartyId = partyId;
        Type = type;
        Status = status;
        LegalName = legalName;
        BirthDetails = birthDetails;
        _contacts = [.. contacts];
        _addresses = [.. addresses];
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        ConcurrencyVersion = concurrencyVersion;
    }

    public InstitutionId InstitutionId { get; }

    public string PartyId { get; }

    public PartyType Type { get; }

    public PartyStatus Status { get; }

    public PersonName LegalName { get; }

    public ProtectedBirthDetails BirthDetails { get; }

    public IReadOnlyList<ProtectedContactPoint> Contacts => _contacts;

    public IReadOnlyList<ProtectedAddress> Addresses => _addresses;

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public long ConcurrencyVersion { get; }

    /// <summary>
    /// Registers an individual party from an already protected capture. A party starts Pending: it
    /// becomes Active only through the approved customer lifecycle, never by being created.
    /// </summary>
    public static Party RegisterIndividual(
        InstitutionId institutionId,
        PersonName legalName,
        ProtectedBirthDetails birthDetails,
        IReadOnlyList<ProtectedContactPoint> contacts,
        IReadOnlyList<ProtectedAddress> addresses,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(legalName);
        ArgumentNullException.ThrowIfNull(birthDetails);
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(addresses);

        if (contacts.Count == 0)
        {
            throw new OnboardingRuleException("onboarding_required_field_missing", "A party requires at least one contact point.");
        }

        if (addresses.Count == 0)
        {
            throw new OnboardingRuleException("onboarding_required_field_missing", "A party requires at least one address.");
        }

        return new Party(
            institutionId,
            Guid.NewGuid().ToString("N"),
            PartyType.Individual,
            PartyStatus.Pending,
            legalName,
            birthDetails,
            contacts,
            addresses,
            now,
            now,
            concurrencyVersion: 1L);
    }

    /// <summary>Rehydrates a persisted party. Used by the persistence layer only.</summary>
    public static Party Restore(
        InstitutionId institutionId,
        string partyId,
        PartyType type,
        PartyStatus status,
        PersonName legalName,
        ProtectedBirthDetails birthDetails,
        IReadOnlyList<ProtectedContactPoint> contacts,
        IReadOnlyList<ProtectedAddress> addresses,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        long concurrencyVersion) =>
        new(institutionId, partyId, type, status, legalName, birthDetails, contacts, addresses, createdAt, updatedAt, concurrencyVersion);
}
