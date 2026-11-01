using System;
using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

/// <summary>
/// A stable party identity. The row holds no personal data itself: the individual attributes, names,
/// contacts and addresses live in their own versioned tables. Decision: epic 03 party model.
/// </summary>
public sealed class PartyRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string PartyId { get; set; } = string.Empty;

    public string PartyType { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long ConcurrencyVersion { get; set; }

    public PartyIndividualRecord? Individual { get; set; }

    public ICollection<PartyNameRecord> Names { get; set; } = [];

    public ICollection<PartyContactRecord> Contacts { get; set; } = [];

    public ICollection<PartyAddressRecord> Addresses { get; set; } = [];
}

/// <summary>The protected individual attributes of a party: one row per party.</summary>
public sealed class PartyIndividualRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string PartyId { get; set; } = string.Empty;

    public string BirthDateToken { get; set; } = string.Empty;

    public string BirthDateLookupToken { get; set; } = string.Empty;

    public string BirthDateMasked { get; set; } = string.Empty;

    public int BirthYear { get; set; }

    public string? CountryOfBirth { get; set; }

    public DateTimeOffset CapturedAt { get; set; }

    public PartyRecord? Party { get; set; }
}

/// <summary>A named identity of a party. A correction adds a row and closes the previous period.</summary>
public sealed class PartyNameRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string NameId { get; set; } = string.Empty;

    public string PartyId { get; set; } = string.Empty;

    public string NameType { get; set; } = string.Empty;

    public string GivenName { get; set; } = string.Empty;

    public string? MiddleName { get; set; }

    public string FamilyName { get; set; } = string.Empty;

    public string Normalised { get; set; } = string.Empty;

    public DateTimeOffset EffectiveFrom { get; set; }

    public DateTimeOffset? EffectiveTo { get; set; }

    public PartyRecord? Party { get; set; }
}

/// <summary>A protected contact point with its masked display form and exact-match lookup token.</summary>
public sealed class PartyContactRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string ContactId { get; set; } = string.Empty;

    public string PartyId { get; set; } = string.Empty;

    public string ContactType { get; set; } = string.Empty;

    public string ValueToken { get; set; } = string.Empty;

    public string MaskedValue { get; set; } = string.Empty;

    public string LookupToken { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }

    public DateTimeOffset? EffectiveTo { get; set; }

    public PartyRecord? Party { get; set; }
}

/// <summary>A protected address: the free-form lines are encrypted, the structured parts are not.</summary>
public sealed class PartyAddressRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string AddressId { get; set; } = string.Empty;

    public string PartyId { get; set; } = string.Empty;

    public string AddressType { get; set; } = string.Empty;

    public string LineToken { get; set; } = string.Empty;

    public string LookupToken { get; set; } = string.Empty;

    public string MaskedLine { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string? Region { get; set; }

    public string CountryCode { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }

    public DateTimeOffset? EffectiveTo { get; set; }

    public PartyRecord? Party { get; set; }
}
