using System;
using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

/// <summary>
/// The KYC case for one application, opened from the requirement snapshot of the submitted revision.
/// Decision: P1-E03-D06, invariant 3.
/// </summary>
public sealed class KycCaseRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string CaseId { get; set; } = string.Empty;

    public string ApplicationId { get; set; } = string.Empty;

    public string PartyId { get; set; } = string.Empty;

    public string RequirementSnapshotId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<KycRequirementRecord> Requirements { get; set; } = [];
}

/// <summary>One required check of a case and its current lifecycle state.</summary>
public sealed class KycRequirementRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string CaseId { get; set; } = string.Empty;

    public string RequirementCode { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? LinkedVerificationId { get; set; }

    public string? WaiverAuthority { get; set; }

    public string? WaiverReason { get; set; }

    public DateTimeOffset? WaivedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public KycCaseRecord? Case { get; set; }
}

/// <summary>A logical canonical verification for one requirement.</summary>
public sealed class IdentityVerificationRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string VerificationId { get; set; } = string.Empty;

    public string CaseId { get; set; } = string.Empty;

    public string ApplicationId { get; set; } = string.Empty;

    public string PartyId { get; set; } = string.Empty;

    public string RequirementCode { get; set; } = string.Empty;

    public string CheckCode { get; set; } = string.Empty;

    public string RequirementSnapshotId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? Outcome { get; set; }

    public string? ProviderMappingVersion { get; set; }

    public string? EvidenceReference { get; set; }

    public string? EvidenceHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<VerificationAttemptRecord> Attempts { get; set; } = [];
}

/// <summary>One immutable provider attempt.</summary>
public sealed class VerificationAttemptRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string VerificationId { get; set; } = string.Empty;

    public string AttemptId { get; set; } = string.Empty;

    public string? ProviderReference { get; set; }

    public string? ProviderRouteId { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset SubmittedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public IdentityVerificationRecord? Verification { get; set; }
}

/// <summary>Append-only verification state evidence.</summary>
public sealed class VerificationStatusHistoryRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string VerificationId { get; set; } = string.Empty;

    public int Sequence { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? Outcome { get; set; }

    public string RecordedBy { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>Append-only requirement state evidence, including reviewer resolutions and waivers.</summary>
public sealed class KycRequirementHistoryRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string CaseId { get; set; } = string.Empty;

    public string RequirementCode { get; set; } = string.Empty;

    public int Sequence { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public string RecordedBy { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }
}
