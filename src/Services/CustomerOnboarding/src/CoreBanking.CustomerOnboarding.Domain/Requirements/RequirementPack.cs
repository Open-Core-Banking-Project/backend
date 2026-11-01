using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CustomerOnboarding.Domain.Requirements;

internal static class RequirementCodes
{
    public static bool IsCode(string? value, int minimumLength = 3, int maximumLength = 64)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        return trimmed.Length >= minimumLength
            && trimmed.Length <= maximumLength
            && trimmed.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')
            && char.IsAsciiLetter(trimmed[0]);
    }
}

/// <summary>
/// One requirement the institution's approved pack asks of an application. The label and the evidence
/// kinds are pack data: no field or check name is authored in code. Decision: P1-E03-D01.
/// </summary>
public sealed record OnboardingRequirement
{
    private const int MaximumLabelLength = 200;
    private const int MaximumEvidenceKinds = 10;

    private OnboardingRequirement(
        string code,
        string label,
        bool mandatory,
        IReadOnlyList<string> evidenceKinds)
    {
        Code = code;
        Label = label;
        Mandatory = mandatory;
        EvidenceKinds = evidenceKinds;
    }

    public string Code { get; }

    public string Label { get; }

    public bool Mandatory { get; }

    public IReadOnlyList<string> EvidenceKinds { get; }

    public static OnboardingRequirement Create(
        string code,
        string label,
        bool mandatory,
        IReadOnlyList<string>? evidenceKinds)
    {
        if (!RequirementCodes.IsCode(code))
        {
            throw new OnboardingRuleException("requirement_code_invalid", "A requirement code must be a stable lowercase code.");
        }

        string trimmedLabel = (label ?? string.Empty).Trim();
        if (trimmedLabel.Length == 0 || trimmedLabel.Length > MaximumLabelLength)
        {
            throw new OnboardingRuleException("requirement_label_invalid", "A requirement requires a display label.");
        }

        IReadOnlyList<string> kinds = evidenceKinds ?? [];
        if (kinds.Count > MaximumEvidenceKinds)
        {
            throw new OnboardingRuleException("requirement_evidence_invalid", "A requirement may name a bounded number of evidence kinds.");
        }

        List<string> normalised = [];
        foreach (string kind in kinds)
        {
            if (!RequirementCodes.IsCode(kind))
            {
                throw new OnboardingRuleException("requirement_evidence_invalid", "An evidence kind must be a stable lowercase code.");
            }

            string trimmed = kind.Trim().ToLowerInvariant();
            if (!normalised.Contains(trimmed, StringComparer.Ordinal))
            {
                normalised.Add(trimmed);
            }
        }

        return new OnboardingRequirement(
            code.Trim().ToLowerInvariant(),
            trimmedLabel,
            mandatory,
            normalised);
    }

    public static OnboardingRequirement Restore(string code, string label, bool mandatory, IReadOnlyList<string> evidenceKinds) =>
        new(code, label, mandatory, [.. evidenceKinds]);
}

/// <summary>
/// An approved, versioned set of onboarding requirements published as institution data. The content
/// hash is what a snapshot cites, so a later reviewer can prove which rules were evaluated.
/// Decision: P1-E03-D01.
/// </summary>
public sealed record RequirementPack
{
    /// <summary>Bounded pack: an unbounded requirement list would be a policy defect, not a data set.</summary>
    public const int MaximumRequirements = 200;

    private RequirementPack(
        string packCode,
        int version,
        string contentHash,
        IReadOnlyList<OnboardingRequirement> requirements)
    {
        PackCode = packCode;
        Version = version;
        ContentHash = contentHash;
        Requirements = requirements;
    }

    public string PackCode { get; }

    public int Version { get; }

    public string ContentHash { get; }

    public IReadOnlyList<OnboardingRequirement> Requirements { get; }

    public IReadOnlyList<OnboardingRequirement> MandatoryRequirements =>
        [.. Requirements.Where(requirement => requirement.Mandatory)];

    public static RequirementPack Create(string packCode, int version, IReadOnlyList<OnboardingRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        if (!RequirementCodes.IsCode(packCode))
        {
            throw new OnboardingRuleException("requirement_pack_invalid", "A requirement pack requires a stable pack code.");
        }

        if (version < 1)
        {
            throw new OnboardingRuleException("requirement_pack_invalid", "A requirement pack version starts at one.");
        }

        if (requirements.Count > MaximumRequirements)
        {
            throw new OnboardingRuleException("requirement_pack_invalid", "A requirement pack may not exceed the bounded requirement count.");
        }

        string[] duplicates = [.. requirements
            .GroupBy(requirement => requirement.Code, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)];
        if (duplicates.Length > 0)
        {
            throw new OnboardingRuleException("requirement_pack_invalid", "A requirement code may appear only once in a pack.");
        }

        string code = packCode.Trim().ToLowerInvariant();
        return new RequirementPack(code, version, ComputeContentHash(code, version, requirements), [.. requirements]);
    }

    public static RequirementPack Restore(string packCode, int version, string contentHash, IReadOnlyList<OnboardingRequirement> requirements) =>
        new(packCode, version, contentHash, [.. requirements]);

    /// <summary>
    /// SHA-256 over the canonical pack form: pack code, version, then each requirement in order with its
    /// mandatory flag and ordered evidence kinds. Requirement order is meaningful, so reordering changes
    /// the hash rather than being silently ignored.
    /// </summary>
    public static string ComputeContentHash(string packCode, int version, IReadOnlyList<OnboardingRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        StringBuilder canonical = new();
        canonical.Append(packCode.Trim().ToLowerInvariant())
            .Append('|')
            .Append(version.ToString(CultureInfo.InvariantCulture));

        foreach (OnboardingRequirement requirement in requirements)
        {
            canonical.Append('\n')
                .Append(requirement.Code)
                .Append(':')
                .Append(requirement.Mandatory ? '1' : '0')
                .Append(':')
                .Append(string.Join(',', requirement.EvidenceKinds));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
}

/// <summary>
/// The exact requirements one application revision was submitted against. It is written once, cites the
/// pack version and hash, and is never edited: a policy change creates new work instead of rewriting
/// history. Decision: P1-E03-D01, invariant 3.
/// </summary>
public sealed class RequirementSnapshot
{
    private readonly List<OnboardingRequirement> _requirements;

    private RequirementSnapshot(
        InstitutionId institutionId,
        string requirementSnapshotId,
        string applicationId,
        int applicationRevision,
        string packCode,
        int packVersion,
        string contentHash,
        IReadOnlyList<OnboardingRequirement> requirements,
        string capturedBy,
        DateTimeOffset capturedAt)
    {
        InstitutionId = institutionId;
        RequirementSnapshotId = requirementSnapshotId;
        ApplicationId = applicationId;
        ApplicationRevision = applicationRevision;
        PackCode = packCode;
        PackVersion = packVersion;
        ContentHash = contentHash;
        _requirements = [.. requirements];
        CapturedBy = capturedBy;
        CapturedAt = capturedAt;
    }

    public InstitutionId InstitutionId { get; }

    public string RequirementSnapshotId { get; }

    public string ApplicationId { get; }

    public int ApplicationRevision { get; }

    public string PackCode { get; }

    public int PackVersion { get; }

    public string ContentHash { get; }

    public IReadOnlyList<OnboardingRequirement> Requirements => _requirements;

    public IReadOnlyList<OnboardingRequirement> MandatoryRequirements =>
        [.. _requirements.Where(requirement => requirement.Mandatory)];

    public string CapturedBy { get; }

    public DateTimeOffset CapturedAt { get; }

    public static RequirementSnapshot Capture(
        InstitutionId institutionId,
        string applicationId,
        int applicationRevision,
        RequirementPack pack,
        string capturedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(capturedBy);

        if (applicationRevision < 1)
        {
            throw new OnboardingRuleException("onboarding_revision_conflict", "A requirement snapshot requires a recorded application revision.");
        }

        return new RequirementSnapshot(
            institutionId,
            Guid.NewGuid().ToString("N"),
            applicationId,
            applicationRevision,
            pack.PackCode,
            pack.Version,
            pack.ContentHash,
            pack.Requirements,
            capturedBy,
            now);
    }

    public static RequirementSnapshot Restore(
        InstitutionId institutionId,
        string requirementSnapshotId,
        string applicationId,
        int applicationRevision,
        string packCode,
        int packVersion,
        string contentHash,
        IReadOnlyList<OnboardingRequirement> requirements,
        string capturedBy,
        DateTimeOffset capturedAt) =>
        new(
            institutionId,
            requirementSnapshotId,
            applicationId,
            applicationRevision,
            packCode,
            packVersion,
            contentHash,
            requirements,
            capturedBy,
            capturedAt);
}
