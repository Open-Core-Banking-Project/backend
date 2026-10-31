using System;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CustomerOnboarding.Domain.Privacy;

/// <summary>A customer's decision for one purpose on one channel.</summary>
public enum PreferenceState
{
    Permitted,
    Denied,
}

/// <summary>Stable codes for the preference state.</summary>
public static class PreferenceCodes
{
    public static string ToCode(PreferenceState state) => state switch
    {
        PreferenceState.Permitted => "permitted",
        PreferenceState.Denied => "denied",
        _ => throw new OnboardingRuleException("preference_state_invalid", $"Preference state '{state}' has no code."),
    };

    public static PreferenceState FromStateCode(string code) => code switch
    {
        "permitted" => PreferenceState.Permitted,
        "denied" => PreferenceState.Denied,
        _ => throw new OnboardingRuleException("preference_state_invalid", $"Preference state code '{code}' is not known."),
    };
}

/// <summary>
/// One effective-dated preference version for a customer, purpose and channel. Purpose, channel, lawful
/// basis and source are approved configuration codes; a granted preference cannot exist without a lawful
/// basis. Decision: P1-E03-D12.
/// </summary>
public sealed class ServicePreference
{
    private const int MaximumCodeLength = 64;

    private ServicePreference(
        InstitutionId institutionId,
        string preferenceId,
        string customerId,
        string purpose,
        string channel,
        int version,
        PreferenceState state,
        string? lawfulBasis,
        string source,
        string? evidenceReference,
        string? reason,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        string recordedBy)
    {
        InstitutionId = institutionId;
        PreferenceId = preferenceId;
        CustomerId = customerId;
        Purpose = purpose;
        Channel = channel;
        Version = version;
        State = state;
        LawfulBasis = lawfulBasis;
        Source = source;
        EvidenceReference = evidenceReference;
        Reason = reason;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        RecordedBy = recordedBy;
    }

    public InstitutionId InstitutionId { get; }

    public string PreferenceId { get; }

    public string CustomerId { get; }

    public string Purpose { get; }

    public string Channel { get; }

    public int Version { get; }

    public PreferenceState State { get; }

    public string? LawfulBasis { get; }

    public string Source { get; }

    public string? EvidenceReference { get; }

    public string? Reason { get; }

    public DateTimeOffset EffectiveFrom { get; }

    public DateTimeOffset? EffectiveTo { get; private set; }

    public string RecordedBy { get; }

    public bool IsCurrent => EffectiveTo is null;

    public static ServicePreference Create(
        InstitutionId institutionId,
        string customerId,
        string purpose,
        string channel,
        int version,
        PreferenceState state,
        string? lawfulBasis,
        string source,
        string? evidenceReference,
        string? reason,
        string recordedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordedBy);

        if (version < 1)
        {
            throw new OnboardingRuleException("preference_version_invalid", "A preference version starts at one.");
        }

        string normalisedBasis = state == PreferenceState.Permitted
            ? ValidateCode(lawfulBasis, "lawfulBasis", "consent_lawful_basis_required")
            : (string.IsNullOrWhiteSpace(lawfulBasis) ? string.Empty : ValidateCode(lawfulBasis, "lawfulBasis", "preference_code_invalid"));

        return new ServicePreference(
            institutionId,
            Guid.NewGuid().ToString("N"),
            customerId.Trim(),
            ValidateCode(purpose, "purpose", "preference_code_invalid"),
            ValidateCode(channel, "channel", "preference_code_invalid"),
            version,
            state,
            string.IsNullOrEmpty(normalisedBasis) ? null : normalisedBasis,
            ValidateCode(source, "source", "preference_code_invalid"),
            string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim(),
            string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            now,
            effectiveTo: null,
            recordedBy.Trim());
    }

    public static ServicePreference Restore(
        InstitutionId institutionId,
        string preferenceId,
        string customerId,
        string purpose,
        string channel,
        int version,
        PreferenceState state,
        string? lawfulBasis,
        string source,
        string? evidenceReference,
        string? reason,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        string recordedBy) =>
        new(
            institutionId,
            preferenceId,
            customerId,
            purpose,
            channel,
            version,
            state,
            lawfulBasis,
            source,
            evidenceReference,
            reason,
            effectiveFrom,
            effectiveTo,
            recordedBy);

    /// <summary>Closes this version when a newer decision replaces it. Closed versions are never edited again.</summary>
    public void Close(DateTimeOffset now)
    {
        if (!IsCurrent)
        {
            throw new OnboardingRuleException("preference_version_closed", "This preference version is already closed.");
        }

        if (now < EffectiveFrom)
        {
            throw new OnboardingRuleException("preference_version_invalid", "A preference version cannot close before it starts.");
        }

        EffectiveTo = now;
    }

    private static string ValidateCode(string? value, string field, string missingCode)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new OnboardingRuleException(missingCode, $"A preference {field} code is required.");
        }

        bool valid = trimmed.Length <= MaximumCodeLength
            && char.IsAsciiLetter(trimmed[0])
            && trimmed.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

        if (!valid)
        {
            throw new OnboardingRuleException(
                "preference_code_invalid",
                $"A preference {field} must be a stable code supplied by approved configuration.");
        }

        return trimmed.ToLowerInvariant();
    }
}

/// <summary>
/// Append-only consent evidence for one preference version. It keeps the purpose, state, lawful basis,
/// source and actor that were true at the time, so a later change cannot rewrite history.
/// Decision: P1-E03-D12.
/// </summary>
public sealed record ConsentEvidence(
    InstitutionId InstitutionId,
    string ConsentId,
    string CustomerId,
    string Purpose,
    string Channel,
    PreferenceState State,
    string? LawfulBasis,
    string Source,
    int Version,
    string? EvidenceReference,
    string RecordedBy,
    DateTimeOffset RecordedAt,
    DateTimeOffset? WithdrawnAt);
