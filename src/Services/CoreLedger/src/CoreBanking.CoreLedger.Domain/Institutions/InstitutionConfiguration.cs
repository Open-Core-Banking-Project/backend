using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Domain.Institutions;

public enum ConfigurationVersionStatus
{
    Draft,
    Submitted,
    Approved,
    Published,
    Superseded,
}

public sealed class ConfigurationContent
{
    private const int MaximumUtf8Bytes = 65_536;

    private static readonly HashSet<string> ProhibitedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "api_key",
        "apikey",
        "credential",
        "password",
        "private_key",
        "privatekey",
        "secret",
        "token",
    };

    private ConfigurationContent(string canonicalJson, string hash)
    {
        CanonicalJson = canonicalJson;
        Hash = hash;
    }

    public string CanonicalJson { get; }
    public string Hash { get; }

    public static ConfigurationContent Create(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumUtf8Bytes)
            throw new ArgumentException("Configuration content exceeds the 64 KiB limit.", nameof(json));

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Configuration content must be a JSON object.", nameof(json));

            ValidateNoSecretMaterial(document.RootElement);
            ArrayBufferWriter<byte> buffer = new();
            using (Utf8JsonWriter writer = new(buffer))
            {
                WriteCanonical(document.RootElement, writer);
            }

            string canonicalJson = Encoding.UTF8.GetString(buffer.WrittenSpan);
            string hash = Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
            return new ConfigurationContent(canonicalJson, hash);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Configuration content must be valid JSON.", nameof(json), exception);
        }
    }

    private static void ValidateNoSecretMaterial(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string normalised = property.Name.Replace("-", "_", StringComparison.Ordinal);
                if (ProhibitedPropertyNames.Contains(normalised))
                    throw new ArgumentException($"Configuration property '{property.Name}' may contain secret material and is prohibited.", nameof(element));
                ValidateNoSecretMaterial(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray()) ValidateNoSecretMaterial(item);
        }
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray()) WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}

public sealed record ConfigurationSimulationEvidence(
    string SimulationId,
    string ContentHash,
    string Engine,
    string Outcome,
    DateTimeOffset SimulatedAt);

public sealed record ConfigurationEffectivePeriod
{
    private ConfigurationEffectivePeriod(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo)
    {
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public DateTimeOffset EffectiveFrom { get; }
    public DateTimeOffset? EffectiveTo { get; }

    public static ConfigurationEffectivePeriod Create(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo = null)
    {
        if (effectiveTo <= effectiveFrom)
            throw new ArgumentException("Effective-to must be later than effective-from.", nameof(effectiveTo));
        return new ConfigurationEffectivePeriod(effectiveFrom, effectiveTo);
    }

    public bool Contains(DateTimeOffset instant) =>
        instant >= EffectiveFrom && (EffectiveTo is null || instant < EffectiveTo.Value);

    public bool Overlaps(ConfigurationEffectivePeriod other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return (EffectiveTo is null || other.EffectiveFrom < EffectiveTo.Value)
            && (other.EffectiveTo is null || EffectiveFrom < other.EffectiveTo.Value);
    }
}

public sealed record PublishedConfigurationVersion(
    string VersionId,
    ConfigurationEffectivePeriod EffectivePeriod);

public static class ConfigurationPublicationPolicy
{
    public static void EnsureNoOverlap(
        IEnumerable<PublishedConfigurationVersion> existingPublications,
        ConfigurationEffectivePeriod proposedPeriod)
    {
        ArgumentNullException.ThrowIfNull(existingPublications);
        ArgumentNullException.ThrowIfNull(proposedPeriod);
        if (existingPublications.Any(x => x.EffectivePeriod.Overlaps(proposedPeriod)))
            throw new InvalidOperationException("configuration_publication_period_overlaps");
    }

    public static PublishedConfigurationVersion ResolveEffective(
        IEnumerable<PublishedConfigurationVersion> publications,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(publications);
        PublishedConfigurationVersion[] matches = publications
            .Where(x => x.EffectivePeriod.Contains(asOf))
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            0 => throw new InvalidOperationException("configuration_not_effective"),
            1 => matches[0],
            _ => throw new InvalidOperationException("configuration_publication_period_overlaps"),
        };
    }
}

public sealed class InstitutionConfigurationVersion
{
    private InstitutionConfigurationVersion(
        InstitutionId institutionId,
        string configurationSetId,
        string versionId,
        int versionNumber,
        string area,
        string scope,
        ConfigurationContent content,
        ActorId createdBy,
        string? rollbackOfVersionId)
    {
        InstitutionId = institutionId;
        ConfigurationSetId = configurationSetId;
        VersionId = versionId;
        VersionNumber = versionNumber;
        Area = area;
        Scope = scope;
        Content = content;
        CreatedBy = createdBy;
        RollbackOfVersionId = rollbackOfVersionId;
    }

    public InstitutionId InstitutionId { get; }
    public string ConfigurationSetId { get; }
    public string VersionId { get; }
    public int VersionNumber { get; }
    public string Area { get; }
    public string Scope { get; }
    public ConfigurationContent Content { get; }
    public ActorId CreatedBy { get; }
    public string? RollbackOfVersionId { get; }
    public ConfigurationVersionStatus Status { get; private set; } = ConfigurationVersionStatus.Draft;
    public ConfigurationSimulationEvidence? Simulation { get; private set; }
    public ActorId? ApprovedBy { get; private set; }
    public string? ApprovalReference { get; private set; }
    public DateTimeOffset? EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveTo { get; private set; }

    public static InstitutionConfigurationVersion Create(
        InstitutionId institutionId,
        string configurationSetId,
        string versionId,
        int versionNumber,
        string area,
        string scope,
        ConfigurationContent content,
        ActorId createdBy,
        string? rollbackOfVersionId = null)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationSetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        ArgumentOutOfRangeException.ThrowIfLessThan(versionNumber, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(createdBy);

        return new InstitutionConfigurationVersion(
            institutionId,
            configurationSetId.Trim(),
            versionId.Trim(),
            versionNumber,
            area.Trim().ToLowerInvariant(),
            scope.Trim(),
            content,
            createdBy,
            string.IsNullOrWhiteSpace(rollbackOfVersionId) ? null : rollbackOfVersionId.Trim());
    }

    public void RecordSimulation(ConfigurationSimulationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        EnsureStatus(ConfigurationVersionStatus.Draft, "Only a draft configuration can be simulated.");
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence.SimulationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence.Engine);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence.Outcome);
        if (!string.Equals(evidence.ContentHash, Content.Hash, StringComparison.Ordinal))
            throw new InvalidOperationException("Simulation evidence does not match the exact configuration content hash.");
        Simulation = evidence;
    }

    public void Submit(ActorId actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        EnsureStatus(ConfigurationVersionStatus.Draft, "Only a draft configuration can be submitted.");
        if (Simulation is null || !string.Equals(Simulation.ContentHash, Content.Hash, StringComparison.Ordinal))
            throw new InvalidOperationException("The exact configuration content must be simulated before submission.");
        if (!string.Equals(Simulation.Outcome, "passed", StringComparison.Ordinal))
            throw new InvalidOperationException("Configuration simulation must pass before submission.");
        Status = ConfigurationVersionStatus.Submitted;
    }

    public void Approve(ActorId approver, string approvalReference)
    {
        ArgumentNullException.ThrowIfNull(approver);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalReference);
        EnsureStatus(ConfigurationVersionStatus.Submitted, "Only a submitted configuration can be approved.");
        EnsureIndependentActor(approver, "The configuration maker cannot approve their own version.");
        ApprovedBy = approver;
        ApprovalReference = approvalReference.Trim();
        Status = ConfigurationVersionStatus.Approved;
    }

    public void Publish(
        ActorId publisher,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        IEnumerable<PublishedConfigurationVersion> existingPublications)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        EnsureStatus(ConfigurationVersionStatus.Approved, "Only an approved configuration can be published.");
        EnsureIndependentActor(publisher, "The configuration maker cannot publish their own version.");
        ConfigurationEffectivePeriod period = ConfigurationEffectivePeriod.Create(effectiveFrom, effectiveTo);
        ConfigurationPublicationPolicy.EnsureNoOverlap(existingPublications, period);
        EffectiveFrom = period.EffectiveFrom;
        EffectiveTo = period.EffectiveTo;
        Status = ConfigurationVersionStatus.Published;
    }

    public void Supersede()
    {
        EnsureStatus(ConfigurationVersionStatus.Published, "Only a published configuration can be superseded.");
        Status = ConfigurationVersionStatus.Superseded;
    }

    public InstitutionConfigurationVersion CreateRollbackDraft(
        string newVersionId,
        int newVersionNumber,
        ActorId actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (Status is not ConfigurationVersionStatus.Published and not ConfigurationVersionStatus.Superseded)
            throw new InvalidOperationException("Rollback can only start from immutable published configuration.");
        return Create(
            InstitutionId,
            ConfigurationSetId,
            newVersionId,
            newVersionNumber,
            Area,
            Scope,
            Content,
            actor,
            VersionId);
    }

    private void EnsureIndependentActor(ActorId actor, string message)
    {
        if (actor == CreatedBy) throw new InvalidOperationException(message);
    }

    private void EnsureStatus(ConfigurationVersionStatus expected, string message)
    {
        if (Status != expected) throw new InvalidOperationException(message);
    }
}
