using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Requirements;

/// <summary>
/// Storage form of a requirement pack and its snapshots. Requirements are data: they are stored as an
/// ordered JSON list of codes, labels, mandatory flags and evidence kinds, never as code constants.
/// Decision: P1-E03-D01.
/// </summary>
internal static class RequirementPayload
{
    private static readonly JsonSerializerOptions SerialiserOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialise(IReadOnlyList<OnboardingRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        return JsonSerializer.Serialize(
            requirements.Select(requirement => new RequirementItem(
                requirement.Code,
                requirement.Label,
                requirement.Mandatory,
                requirement.EvidenceKinds)),
            SerialiserOptions);
    }

    public static IReadOnlyList<OnboardingRequirement> Deserialise(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new OnboardingRuleException("requirement_pack_invalid", "A stored requirement list is empty.");
        }

        List<RequirementItem>? items = JsonSerializer.Deserialize<List<RequirementItem>>(json, SerialiserOptions);
        if (items is null)
        {
            throw new OnboardingRuleException("requirement_pack_invalid", "A stored requirement list could not be read.");
        }

        return
        [
            .. items.Select(item => OnboardingRequirement.Restore(item.Code, item.Label, item.Mandatory, item.EvidenceKinds ?? [])),
        ];
    }

    public static RequirementPack ToPack(RequirementPackRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return RequirementPack.Restore(
            record.PackCode,
            record.Version,
            record.ContentHash,
            Deserialise(record.Requirements));
    }

    public static RequirementSnapshotRecord ToRecord(RequirementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new RequirementSnapshotRecord
        {
            InstitutionId = snapshot.InstitutionId.Value,
            RequirementSnapshotId = snapshot.RequirementSnapshotId,
            ApplicationId = snapshot.ApplicationId,
            ApplicationRevision = snapshot.ApplicationRevision,
            PackCode = snapshot.PackCode,
            PackVersion = snapshot.PackVersion,
            ContentHash = snapshot.ContentHash,
            Requirements = Serialise(snapshot.Requirements),
            CapturedBy = snapshot.CapturedBy,
            CapturedAt = snapshot.CapturedAt,
        };
    }

    public static RequirementSnapshot ToModel(RequirementSnapshotRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return RequirementSnapshot.Restore(
            InstitutionId.From(record.InstitutionId),
            record.RequirementSnapshotId,
            record.ApplicationId,
            record.ApplicationRevision,
            record.PackCode,
            record.PackVersion,
            record.ContentHash,
            Deserialise(record.Requirements),
            record.CapturedBy,
            record.CapturedAt);
    }

    private sealed record RequirementItem(
        string Code,
        string Label,
        bool Mandatory,
        IReadOnlyList<string>? EvidenceKinds);
}
