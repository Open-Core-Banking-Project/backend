using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain.Requirements;

namespace CoreBanking.CustomerOnboarding.Application.Requirements;

/// <summary>
/// Malware-scan lifecycle owned by the approved document storage component. Customer and Onboarding
/// records the reference and the outcome; it never treats an unchecked document as clean.
/// Decision: P1-E03-D01, epic 03 ownership table.
/// </summary>
public enum DocumentScanStatus
{
    PendingScan,
    Clean,
    Rejected,
}

public sealed record DocumentScanRequest(
    string InstitutionId,
    string ApplicationId,
    string RequirementCode,
    string ContentHash,
    long ContentLength,
    string ContentType,
    string StorageReference);

public sealed record DocumentScanResult(DocumentScanStatus Status, string? ProviderReference);

/// <summary>
/// Port for the document quarantine component. Implementations must fail closed: an unanswered or
/// unknown outcome leaves the document pending, never clean.
/// </summary>
public interface IDocumentQuarantine
{
    Task<DocumentScanResult> SubmitAsync(DocumentScanRequest request, CancellationToken cancellationToken);
}

/// <summary>Publishes the next version of the institution's approved requirement pack.</summary>
public sealed record PublishRequirementPackCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string PackCode,
    IReadOnlyList<OnboardingRequirement> Requirements,
    string CorrelationId);

/// <summary>Registers document metadata and submits the reference to quarantine. No binary content flows here.</summary>
public sealed record RegisterDocumentCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string ApplicationId,
    string RequirementCode,
    string ContentType,
    long ContentLength,
    string ContentHash,
    string StorageReference,
    string CorrelationId);

/// <summary>Reads one snapshot. Invisible outside the caller's institution.</summary>
public sealed record GetRequirementSnapshotQuery(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string RequirementSnapshotId);

public sealed record RequirementView(string Code, string Label, bool Mandatory, IReadOnlyList<string> EvidenceKinds);

public sealed record RequirementPackView(
    string PackCode,
    int Version,
    string ContentHash,
    DateTimeOffset PublishedAt,
    string PublishedBy,
    IReadOnlyList<RequirementView> Requirements);

public sealed record RequirementSnapshotView(
    string RequirementSnapshotId,
    string ApplicationId,
    int ApplicationRevision,
    string PackCode,
    int PackVersion,
    string ContentHash,
    DateTimeOffset CapturedAt,
    string CapturedBy,
    IReadOnlyList<RequirementView> Requirements);

public sealed record DocumentReferenceView(
    string DocumentReferenceId,
    string ApplicationId,
    string RequirementCode,
    string ContentType,
    long ContentLength,
    string ContentHash,
    string ScanStatus,
    DateTimeOffset RegisteredAt);

public interface IRequirementService
{
    Task<RequirementPackView> PublishPackAsync(PublishRequirementPackCommand command, CancellationToken cancellationToken);

    Task<DocumentReferenceView> RegisterDocumentAsync(RegisterDocumentCommand command, CancellationToken cancellationToken);

    Task<RequirementSnapshotView> GetSnapshotAsync(GetRequirementSnapshotQuery query, CancellationToken cancellationToken);
}

/// <summary>The snapshot does not exist in the caller's tenant, or is not visible to the caller.</summary>
public sealed class RequirementSnapshotNotFoundException : Exception
{
    public RequirementSnapshotNotFoundException(string requirementSnapshotId)
        : base($"Requirement snapshot '{requirementSnapshotId}' was not found.") => RequirementSnapshotId = requirementSnapshotId;

    public string RequirementSnapshotId { get; }
}
