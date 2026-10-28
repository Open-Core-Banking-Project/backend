using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Onboarding;

/// <summary>
/// The stored form of a capture. Every personal value is already a protected token, so this payload
/// is what a revision row holds and what staff display reads, never the plaintext. Decision: CUS-028.
/// </summary>
public sealed record CapturePayload
{
    public CaptureNamePayload? Name { get; init; }

    public CaptureBirthPayload? Birth { get; init; }

    public IReadOnlyList<CaptureContactPayload> Contacts { get; init; } = [];

    public IReadOnlyList<CaptureAddressPayload> Addresses { get; init; } = [];
}

public sealed record CaptureNamePayload
{
    public string GivenName { get; init; } = string.Empty;

    public string? MiddleName { get; init; }

    public string FamilyName { get; init; } = string.Empty;
}

public sealed record CaptureBirthPayload
{
    public string BirthDateToken { get; init; } = string.Empty;

    public string BirthDateMasked { get; init; } = string.Empty;

    public int BirthYear { get; init; }

    public string? CountryOfBirth { get; init; }
}

public sealed record CaptureContactPayload
{
    public string Type { get; init; } = string.Empty;

    public string ValueToken { get; init; } = string.Empty;

    public string MaskedValue { get; init; } = string.Empty;

    public string LookupToken { get; init; } = string.Empty;
}

public sealed record CaptureAddressPayload
{
    public string Type { get; init; } = string.Empty;

    public string LineToken { get; init; } = string.Empty;

    public string MaskedLine { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string? Region { get; init; }

    public string CountryCode { get; init; } = string.Empty;
}

/// <summary>
/// Maps a validated capture to its protected payload, and a payload back to the values a party or a
/// completeness check needs. The payload hash is what a revision is identified by, so two different
/// captures can never share a hash by accident. Decision: CUS-UC-01.
/// </summary>
public static class CapturePayloadFactory
{
    private static readonly JsonSerializerOptions SerialiserOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    public static CapturePayload Protect(OnboardingCapture capture, IProtectedValueProtector protector)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(protector);

        return new CapturePayload
        {
            Name = capture.LegalName is null
                ? null
                : new CaptureNamePayload
                {
                    GivenName = capture.LegalName.GivenName,
                    MiddleName = capture.LegalName.MiddleName,
                    FamilyName = capture.LegalName.FamilyName,
                },
            Birth = capture.BirthDetails is null
                ? null
                : ProtectBirth(capture.BirthDetails, protector),
            Contacts = [.. capture.Contacts.Select(contact => ProtectContact(contact, protector))],
            Addresses = [.. capture.Addresses.Select(address => ProtectAddress(address, protector))],
        };
    }

    public static string Serialise(CapturePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return JsonSerializer.Serialize(payload, SerialiserOptions);
    }

    public static CapturePayload Deserialise(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        CapturePayload? payload = JsonSerializer.Deserialize<CapturePayload>(json, SerialiserOptions);
        return payload ?? throw new InvalidOperationException("The stored capture payload is empty.");
    }

    /// <summary>SHA-256 of the exact serialised payload, hex-encoded.</summary>
    public static string Hash(CapturePayload payload) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Serialise(payload))));

    public static OnboardingCaptureSnapshot ToSnapshot(CapturePayload payload, DateTimeOffset capturedAt) =>
        new(
            payload.Name is null
                ? null
                : PersonName.Create(payload.Name.GivenName, payload.Name.MiddleName, payload.Name.FamilyName),
            payload.Birth is null
                ? null
                : new ProtectedBirthDetails(
                    payload.Birth.BirthDateToken,
                    payload.Birth.BirthDateMasked,
                    payload.Birth.BirthYear,
                    payload.Birth.CountryOfBirth,
                    capturedAt),
            [.. payload.Contacts.Select(contact => new ProtectedContactPoint(
                ParseEnum<ContactPointType>(contact.Type),
                contact.ValueToken,
                contact.MaskedValue,
                contact.LookupToken,
                capturedAt))],
            [.. payload.Addresses.Select(address => new ProtectedAddress(
                ParseEnum<AddressType>(address.Type),
                address.LineToken,
                address.MaskedLine,
                address.City,
                address.Region,
                address.CountryCode,
                capturedAt))]);

    /// <summary>Creates the pending party from a complete payload.</summary>
    public static Party ToParty(CapturePayload payload, InstitutionId institutionId, DateTimeOffset now)
    {
        OnboardingCaptureSnapshot snapshot = ToSnapshot(payload, now);

        if (!snapshot.IsComplete)
        {
            throw new OnboardingRuleException(
                "onboarding_required_field_missing",
                $"The application is incomplete: {string.Join(", ", snapshot.MissingFields())}.");
        }

        return Party.RegisterIndividual(
            institutionId,
            snapshot.LegalName!,
            snapshot.BirthDetails!,
            snapshot.Contacts,
            snapshot.Addresses,
            now);
    }

    private static CaptureBirthPayload ProtectBirth(BirthDetails birthDetails, IProtectedValueProtector protector)
    {
        string iso = birthDetails.BirthDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        ProtectedValue value = protector.Protect(iso);

        return new CaptureBirthPayload
        {
            BirthDateToken = value.ValueToken,
            BirthDateMasked = birthDetails.BirthDate.ToString("yyyy-MM-**", System.Globalization.CultureInfo.InvariantCulture),
            BirthYear = birthDetails.BirthDate.Year,
            CountryOfBirth = birthDetails.CountryOfBirth,
        };
    }

    private static CaptureContactPayload ProtectContact(ContactPoint contact, IProtectedValueProtector protector)
    {
        ProtectedValue value = protector.Protect(contact.Value);

        return new CaptureContactPayload
        {
            Type = contact.Type.ToString(),
            ValueToken = value.ValueToken,
            MaskedValue = value.MaskedValue,
            LookupToken = value.LookupToken,
        };
    }

    private static CaptureAddressPayload ProtectAddress(Address address, IProtectedValueProtector protector)
    {
        string lines = address.Line2 is null ? address.Line1 : $"{address.Line1}\n{address.Line2}";
        ProtectedValue value = protector.Protect(lines);

        return new CaptureAddressPayload
        {
            Type = address.Type.ToString(),
            LineToken = value.ValueToken,
            MaskedLine = value.MaskedValue,
            City = address.City,
            Region = address.Region,
            CountryCode = address.CountryCode,
        };
    }

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse(value, ignoreCase: true, out TEnum parsed))
        {
            throw new InvalidOperationException($"The stored capture contains an unsupported {typeof(TEnum).Name} value.");
        }

        return parsed;
    }
}
