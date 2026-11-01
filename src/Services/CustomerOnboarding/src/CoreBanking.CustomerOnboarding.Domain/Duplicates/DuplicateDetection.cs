using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CustomerOnboarding.Domain.Duplicates;

/// <summary>
/// The families of exact match Phase 1 can produce. A verified-identifier family is deliberately
/// absent until identifiers are captured, so detection cannot claim a match it cannot evidence.
/// Decision: P1-E03-D04-A.
/// </summary>
public enum DuplicateMatchFactorKind
{
    ContactPoint,
    NameAndBirthDate,
    AddressLine,
}

/// <summary>
/// Ordered presentation bands. A band is derived from the factor families, never supplied, and it is
/// not a risk class, a rejection rule or an approval shortcut. Decision: P1-E03-D04-B.
/// </summary>
public enum DuplicateMatchBand
{
    Possible,
    Probable,

    /// <summary>Reserved for a verified-identifier match; unreachable until identifiers are captured.</summary>
    Confirmed,
}

/// <summary>Candidate set state. A superseded set is retained as evidence, never deleted.</summary>
public enum DuplicateCandidateSetStatus
{
    Open,
    Decided,
    Superseded,
}

/// <summary>A reviewer outcome. None of these merge, reject or block a customer. Decision: P1-E03-D04-C.</summary>
public enum DuplicateReviewDecision
{
    SamePerson,
    Distinct,
    InsufficientEvidence,
}

/// <summary>
/// The stable wire and storage codes for the duplicate enums. Codes are lowercase snake case so a
/// stored rule version and a rendered problem detail never depend on the CLR enum names.
/// </summary>
public static class DuplicateCodes
{
    public const string Possible = "possible";
    public const string Probable = "probable";
    public const string Confirmed = "confirmed";

    public const string Open = "open";
    public const string Decided = "decided";
    public const string Superseded = "superseded";

    public const string SamePerson = "same_person";
    public const string Distinct = "distinct";
    public const string InsufficientEvidence = "insufficient_evidence";

    public const string ContactPoint = "contact_point";
    public const string NameAndBirthDate = "name_and_birth_date";
    public const string AddressLine = "address_line";

    public static string ToCode(DuplicateMatchFactorKind kind) => kind switch
    {
        DuplicateMatchFactorKind.ContactPoint => ContactPoint,
        DuplicateMatchFactorKind.NameAndBirthDate => NameAndBirthDate,
        DuplicateMatchFactorKind.AddressLine => AddressLine,
        _ => throw new OnboardingRuleException("duplicate_factor_invalid", $"Match factor kind '{kind}' has no code."),
    };

    public static DuplicateMatchFactorKind FromKindCode(string code) => code switch
    {
        ContactPoint => DuplicateMatchFactorKind.ContactPoint,
        NameAndBirthDate => DuplicateMatchFactorKind.NameAndBirthDate,
        AddressLine => DuplicateMatchFactorKind.AddressLine,
        _ => throw new OnboardingRuleException("duplicate_factor_invalid", $"Match factor code '{code}' is not known."),
    };

    public static string ToCode(DuplicateMatchBand band) => band switch
    {
        DuplicateMatchBand.Possible => Possible,
        DuplicateMatchBand.Probable => Probable,
        DuplicateMatchBand.Confirmed => Confirmed,
        _ => throw new OnboardingRuleException("duplicate_band_invalid", $"Match band '{band}' has no code."),
    };

    public static DuplicateMatchBand FromBandCode(string code) => code switch
    {
        Possible => DuplicateMatchBand.Possible,
        Probable => DuplicateMatchBand.Probable,
        Confirmed => DuplicateMatchBand.Confirmed,
        _ => throw new OnboardingRuleException("duplicate_band_invalid", $"Match band code '{code}' is not known."),
    };

    public static string ToCode(DuplicateCandidateSetStatus status) => status switch
    {
        DuplicateCandidateSetStatus.Open => Open,
        DuplicateCandidateSetStatus.Decided => Decided,
        DuplicateCandidateSetStatus.Superseded => Superseded,
        _ => throw new OnboardingRuleException("duplicate_candidate_set_status_invalid", $"Candidate set status '{status}' has no code."),
    };

    public static DuplicateCandidateSetStatus FromStatusCode(string code) => code switch
    {
        Open => DuplicateCandidateSetStatus.Open,
        Decided => DuplicateCandidateSetStatus.Decided,
        Superseded => DuplicateCandidateSetStatus.Superseded,
        _ => throw new OnboardingRuleException("duplicate_candidate_set_status_invalid", $"Candidate set status code '{code}' is not known."),
    };

    public static string ToCode(DuplicateReviewDecision decision) => decision switch
    {
        DuplicateReviewDecision.SamePerson => SamePerson,
        DuplicateReviewDecision.Distinct => Distinct,
        DuplicateReviewDecision.InsufficientEvidence => InsufficientEvidence,
        _ => throw new OnboardingRuleException("duplicate_decision_invalid", $"Review decision '{decision}' has no code."),
    };

    public static DuplicateReviewDecision FromDecisionCode(string code) => code switch
    {
        SamePerson => DuplicateReviewDecision.SamePerson,
        Distinct => DuplicateReviewDecision.Distinct,
        InsufficientEvidence => DuplicateReviewDecision.InsufficientEvidence,
        _ => throw new OnboardingRuleException("duplicate_decision_invalid", $"Review decision code '{code}' is not known."),
    };
}

/// <summary>
/// One explainable reason a candidate was raised. The basis is a stable code, not a value: a stored
/// factor never contains a raw identifier, contact value or address line. Decision: P1-E03-D04-A.
/// </summary>
public sealed record DuplicateMatchFactor
{
    public const string NameAndBirthDateBasis = "name_and_birth_date";
    public const string AddressLineBasis = "address_line";
    private const int MaximumBasisLength = 48;

    private DuplicateMatchFactor(DuplicateMatchFactorKind kind, string basis)
    {
        Kind = kind;
        Basis = basis;
    }

    public DuplicateMatchFactorKind Kind { get; }

    public string Basis { get; }

    public static DuplicateMatchFactor Create(DuplicateMatchFactorKind kind, string basis)
    {
        if (string.IsNullOrWhiteSpace(basis))
        {
            throw new OnboardingRuleException("duplicate_factor_invalid", "A match factor requires a basis code.");
        }

        string trimmed = basis.Trim();
        if (trimmed.Length > MaximumBasisLength || trimmed.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new OnboardingRuleException("duplicate_factor_invalid", "A match factor basis must be a lowercase code.");
        }

        return new DuplicateMatchFactor(kind, trimmed.ToLowerInvariant());
    }
}

/// <summary>A protected contact comparison value as a match input: channel code, lookup token and masked display.</summary>
public sealed record DuplicateContactToken(string TypeCode, string LookupToken, string MaskedValue);

/// <summary>A protected address comparison value as a match input: line token and masked display.</summary>
public sealed record DuplicateAddressToken(string LineToken, string MaskedLine);

/// <summary>
/// The exact-match view of one party. It carries only protected tokens and masked display values, so a
/// comparison can never read or emit plaintext. Decision: P1-E03-D04-A.
/// </summary>
public sealed record DuplicateSubject(
    string PartyId,
    string NormalisedName,
    string BirthDateLookupToken,
    int BirthYear,
    IReadOnlyList<DuplicateContactToken> Contacts,
    IReadOnlyList<DuplicateAddressToken> Addresses);

/// <summary>
/// Exact duplicate rules. Comparison is symmetric, deterministic and explainable, and it produces
/// factors only; the band is derived from those factors so a caller cannot assert confidence.
/// Decision: P1-E03-D04-A, P1-E03-D04-B.
/// </summary>
public static class DuplicateMatchRules
{
    /// <summary>Stored with every candidate set so evidence names the rule that produced it.</summary>
    public const string RuleVersion = "duplicate-exact-v1";

    /// <summary>Bounded detection run: a filter error must not turn into an unbounded scan.</summary>
    public const int MaximumCandidates = 50;

    public static IReadOnlyList<DuplicateMatchFactor> Compare(DuplicateSubject subject, DuplicateSubject candidate)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(candidate);

        if (string.Equals(subject.PartyId, candidate.PartyId, StringComparison.Ordinal))
        {
            return [];
        }

        List<DuplicateMatchFactor> factors = [];

        foreach (DuplicateContactToken contact in subject.Contacts)
        {
            bool matched = candidate.Contacts.Any(other =>
                string.Equals(other.TypeCode, contact.TypeCode, StringComparison.Ordinal)
                && string.Equals(other.LookupToken, contact.LookupToken, StringComparison.Ordinal));
            bool recorded = factors.Any(factor =>
                factor.Kind == DuplicateMatchFactorKind.ContactPoint
                && string.Equals(factor.Basis, contact.TypeCode, StringComparison.Ordinal));

            if (matched && !recorded)
            {
                factors.Add(DuplicateMatchFactor.Create(DuplicateMatchFactorKind.ContactPoint, contact.TypeCode));
            }
        }

        bool sameName = !string.IsNullOrWhiteSpace(subject.NormalisedName)
            && string.Equals(subject.NormalisedName, candidate.NormalisedName, StringComparison.Ordinal);
        bool sameBirthDate = !string.IsNullOrWhiteSpace(subject.BirthDateLookupToken)
            && string.Equals(subject.BirthDateLookupToken, candidate.BirthDateLookupToken, StringComparison.Ordinal);

        if (sameName && sameBirthDate)
        {
            factors.Add(DuplicateMatchFactor.Create(DuplicateMatchFactorKind.NameAndBirthDate, DuplicateMatchFactor.NameAndBirthDateBasis));
        }

        bool sameAddressLine = subject.Addresses.Any(address => !string.IsNullOrEmpty(address.LineToken)
            && candidate.Addresses.Any(other => string.Equals(other.LineToken, address.LineToken, StringComparison.Ordinal)));

        if (sameAddressLine)
        {
            factors.Add(DuplicateMatchFactor.Create(DuplicateMatchFactorKind.AddressLine, DuplicateMatchFactor.AddressLineBasis));
        }

        return factors;
    }

    /// <summary>
    /// The band implied by the factor families. Name together with the full date of birth is probable
    /// on its own; every other single family is possible. A verified identifier is required for
    /// confirmed, so Phase 1 never returns it. Decision: P1-E03-D04-B.
    /// </summary>
    public static DuplicateMatchBand Band(IReadOnlyCollection<DuplicateMatchFactor> factors)
    {
        ArgumentNullException.ThrowIfNull(factors);

        if (factors.Count == 0)
        {
            throw new OnboardingRuleException("duplicate_factors_missing", "A duplicate candidate requires at least one match factor.");
        }

        return factors.Any(factor => factor.Kind == DuplicateMatchFactorKind.NameAndBirthDate)
            ? DuplicateMatchBand.Probable
            : DuplicateMatchBand.Possible;
    }

    /// <summary>
    /// The authorised comparison values for a candidate: masked contact and address forms plus the
    /// birth year, which policy already treats as readable. Decision: P1-E03-D04 section 3.
    /// </summary>
    public static IReadOnlyList<string> MaskedMatches(
        DuplicateSubject subject,
        DuplicateSubject candidate,
        IReadOnlyList<DuplicateMatchFactor> factors)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(factors);

        List<string> masked = [];
        foreach (DuplicateMatchFactor factor in factors)
        {
            switch (factor.Kind)
            {
                case DuplicateMatchFactorKind.ContactPoint:
                    masked.AddRange(subject.Contacts
                        .Where(contact => string.Equals(contact.TypeCode, factor.Basis, StringComparison.Ordinal)
                            && candidate.Contacts.Any(other => string.Equals(other.TypeCode, contact.TypeCode, StringComparison.Ordinal)
                                && string.Equals(other.LookupToken, contact.LookupToken, StringComparison.Ordinal)))
                        .Select(contact => contact.MaskedValue));
                    break;
                case DuplicateMatchFactorKind.NameAndBirthDate:
                    masked.Add("birthYear:" + candidate.BirthYear.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case DuplicateMatchFactorKind.AddressLine:
                    masked.AddRange(subject.Addresses
                        .Where(address => candidate.Addresses.Any(other => string.Equals(other.LineToken, address.LineToken, StringComparison.Ordinal)))
                        .Select(address => address.MaskedLine));
                    break;
            }
        }

        return masked;
    }
}

/// <summary>
/// One party raised as a duplicate candidate. The band is derived from the factors at construction, so
/// a persisted candidate cannot claim a stronger band than its evidence supports.
/// Decision: P1-E03-D04-B.
/// </summary>
public sealed record DuplicateCandidate
{
    private DuplicateCandidate(
        string candidatePartyId,
        DuplicateMatchBand band,
        IReadOnlyList<DuplicateMatchFactor> factors,
        DateTimeOffset detectedAt)
    {
        CandidatePartyId = candidatePartyId;
        Band = band;
        Factors = factors;
        DetectedAt = detectedAt;
    }

    public string CandidatePartyId { get; }

    public DuplicateMatchBand Band { get; }

    public IReadOnlyList<DuplicateMatchFactor> Factors { get; }

    public DateTimeOffset DetectedAt { get; }

    public static DuplicateCandidate Create(string candidatePartyId, IReadOnlyList<DuplicateMatchFactor> factors, DateTimeOffset detectedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePartyId);
        ArgumentNullException.ThrowIfNull(factors);

        DuplicateMatchBand band = DuplicateMatchRules.Band(factors);
        return new DuplicateCandidate(candidatePartyId, band, [.. factors], detectedAt);
    }

    /// <summary>Rehydrates a persisted candidate and re-derives the band from its evidence.</summary>
    public static DuplicateCandidate Restore(string candidatePartyId, IReadOnlyList<DuplicateMatchFactor> factors, DateTimeOffset detectedAt) =>
        Create(candidatePartyId, factors, detectedAt);
}

/// <summary>
/// A detection run over one application revision and its reviewer decision. There is at most one live
/// (open or decided) set per revision: a later run supersedes the earlier open set, which stays
/// readable as evidence. A decision is final for its set. Decision: P1-E03-D04-C, P1-E03-D04-D.
/// </summary>
public sealed class DuplicateCandidateSet
{
    private const int MinimumReasonLength = 3;
    private const int MaximumReasonLength = 500;

    private readonly List<DuplicateCandidate> _candidates;

    private DuplicateCandidateSet(
        InstitutionId institutionId,
        string setId,
        string applicationId,
        int applicationRevision,
        string ruleVersion,
        DuplicateCandidateSetStatus status,
        long version,
        string createdBy,
        IReadOnlyList<DuplicateCandidate> candidates,
        DateTimeOffset createdAt,
        DateTimeOffset? decidedAt,
        string? decidedBy,
        DuplicateReviewDecision? decision,
        string? decisionReason)
    {
        InstitutionId = institutionId;
        SetId = setId;
        ApplicationId = applicationId;
        ApplicationRevision = applicationRevision;
        RuleVersion = ruleVersion;
        Status = status;
        Version = version;
        CreatedBy = createdBy;
        _candidates = [.. candidates];
        CreatedAt = createdAt;
        DecidedAt = decidedAt;
        DecidedBy = decidedBy;
        Decision = decision;
        DecisionReason = decisionReason;
    }

    public InstitutionId InstitutionId { get; }

    public string SetId { get; }

    public string ApplicationId { get; }

    public int ApplicationRevision { get; }

    public string RuleVersion { get; }

    public DuplicateCandidateSetStatus Status { get; private set; }

    public long Version { get; private set; }

    public string CreatedBy { get; }

    /// <summary>The candidates of this run. Empty is a real outcome: detection ran and found none.</summary>
    public IReadOnlyList<DuplicateCandidate> Candidates => _candidates;

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? DecidedBy { get; private set; }

    public DuplicateReviewDecision? Decision { get; private set; }

    public string? DecisionReason { get; private set; }

    public static DuplicateCandidateSet Open(
        InstitutionId institutionId,
        string applicationId,
        int applicationRevision,
        string createdBy,
        IReadOnlyList<DuplicateCandidate> candidates,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);
        ArgumentNullException.ThrowIfNull(candidates);

        if (applicationRevision < 1)
        {
            throw new OnboardingRuleException("onboarding_revision_conflict", "A duplicate detection run requires a recorded application revision.");
        }

        if (candidates.Count > DuplicateMatchRules.MaximumCandidates)
        {
            throw new OnboardingRuleException("duplicate_candidates_too_many", "A detection run may not exceed the bounded candidate count.");
        }

        return new DuplicateCandidateSet(
            institutionId,
            Guid.NewGuid().ToString("N"),
            applicationId,
            applicationRevision,
            DuplicateMatchRules.RuleVersion,
            DuplicateCandidateSetStatus.Open,
            version: 1L,
            createdBy,
            candidates,
            now,
            decidedAt: null,
            decidedBy: null,
            decision: null,
            decisionReason: null);
    }

    /// <summary>Closes an open set because a newer run replaced it. The set and its evidence are retained.</summary>
    public void Supersede(DateTimeOffset now)
    {
        if (Status != DuplicateCandidateSetStatus.Open)
        {
            throw new OnboardingRuleException("duplicate_candidate_set_closed", "Only an open candidate set can be superseded.");
        }

        Status = DuplicateCandidateSetStatus.Superseded;
        Version++;
    }

    public void Decide(
        DuplicateReviewDecision decision,
        string decidedBy,
        string reason,
        long expectedVersion,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);

        if (Status != DuplicateCandidateSetStatus.Open)
        {
            throw new OnboardingRuleException("duplicate_candidate_set_closed", "A decided or superseded candidate set cannot be decided again.");
        }

        if (expectedVersion != Version)
        {
            throw new OnboardingRuleException("duplicate_candidate_set_stale_version", "The candidate set changed since it was read.");
        }

        string trimmedReason = (reason ?? string.Empty).Trim();
        if (trimmedReason.Length < MinimumReasonLength || trimmedReason.Length > MaximumReasonLength)
        {
            throw new OnboardingRuleException("duplicate_decision_reason_required", "A reviewer decision requires a recorded reason.");
        }

        Status = DuplicateCandidateSetStatus.Decided;
        DecidedAt = now;
        DecidedBy = decidedBy;
        Decision = decision;
        DecisionReason = trimmedReason;
        Version++;
    }

    public static DuplicateCandidateSet Restore(
        InstitutionId institutionId,
        string setId,
        string applicationId,
        int applicationRevision,
        string ruleVersion,
        DuplicateCandidateSetStatus status,
        long version,
        string createdBy,
        IReadOnlyList<DuplicateCandidate> candidates,
        DateTimeOffset createdAt,
        DateTimeOffset? decidedAt,
        string? decidedBy,
        DuplicateReviewDecision? decision,
        string? decisionReason) =>
        new(
            institutionId,
            setId,
            applicationId,
            applicationRevision,
            ruleVersion,
            status,
            version,
            createdBy,
            candidates,
            createdAt,
            decidedAt,
            decidedBy,
            decision,
            decisionReason);
}
