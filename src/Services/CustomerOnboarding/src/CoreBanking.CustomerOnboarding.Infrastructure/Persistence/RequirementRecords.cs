using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

/// <summary>
/// One published version of the institution's approved onboarding requirement pack. Content is data
/// owned by Product and Compliance; a new version supersedes the previous one and no version is edited.
/// Decision: P1-E03-D01.
/// </summary>
public sealed class RequirementPackRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string PackCode { get; set; } = string.Empty;

    public int Version { get; set; }

    public string ContentHash { get; set; } = string.Empty;

    public string Requirements { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string PublishedBy { get; set; } = string.Empty;

    public DateTimeOffset PublishedAt { get; set; }

    public DateTimeOffset? SupersededAt { get; set; }
}

/// <summary>
/// The requirements one application revision was submitted against. Written once, never edited, and it
/// cites the exact pack version and hash so later policy changes cannot rewrite history.
/// Decision: P1-E03-D01, invariant 3.
/// </summary>
public sealed class RequirementSnapshotRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string RequirementSnapshotId { get; set; } = string.Empty;

    public string ApplicationId { get; set; } = string.Empty;

    public int ApplicationRevision { get; set; }

    public string PackCode { get; set; } = string.Empty;

    public int PackVersion { get; set; }

    public string ContentHash { get; set; } = string.Empty;

    public string Requirements { get; set; } = string.Empty;

    public string CapturedBy { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; }

    public OnboardingApplicationRecord? Application { get; set; }
}

/// <summary>
/// Document metadata and its quarantine outcome. No binary content is stored here: only the opaque
/// storage reference, the hash and the scan state. Decision: P1-E03-D01 (ownership table).
/// </summary>
public sealed class DocumentReferenceRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string DocumentReferenceId { get; set; } = string.Empty;

    public string ApplicationId { get; set; } = string.Empty;

    public string RequirementCode { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long ContentLength { get; set; }

    public string ContentHash { get; set; } = string.Empty;

    public string StorageReference { get; set; } = string.Empty;

    public string ScanStatus { get; set; } = string.Empty;

    public string? ProviderReference { get; set; }

    public string RegisteredBy { get; set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; set; }
}
