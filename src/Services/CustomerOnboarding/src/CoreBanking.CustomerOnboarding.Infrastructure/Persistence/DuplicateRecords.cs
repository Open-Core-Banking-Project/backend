using System;
using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

/// <summary>
/// A duplicate detection run over one application revision. The row records the rule version and the
/// reviewer decision, and a superseded run is kept rather than deleted. It holds no personal data:
/// the factors live on the candidate rows as codes. Decision: P1-E03-D04.
/// </summary>
public sealed class DuplicateCandidateSetRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string SetId { get; set; } = string.Empty;

    public string ApplicationId { get; set; } = string.Empty;

    public int ApplicationRevision { get; set; }

    public string RuleVersion { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public long Version { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecidedBy { get; set; }

    public string? Decision { get; set; }

    public string? DecisionReason { get; set; }

    public ICollection<DuplicateCandidateRecord> Candidates { get; set; } = [];
}

/// <summary>
/// One candidate party and the exact match factors that raised it. Factors are stored as codes so a
/// row can never hold a raw identifier, contact value or address line. Decision: P1-E03-D04-A.
/// </summary>
public sealed class DuplicateCandidateRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string SetId { get; set; } = string.Empty;

    public string CandidatePartyId { get; set; } = string.Empty;

    public string Band { get; set; } = string.Empty;

    public string Factors { get; set; } = string.Empty;

    public DateTimeOffset DetectedAt { get; set; }

    public DuplicateCandidateSetRecord? Set { get; set; }
}
