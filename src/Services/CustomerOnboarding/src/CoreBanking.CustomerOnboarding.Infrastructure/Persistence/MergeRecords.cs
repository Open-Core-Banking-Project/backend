using System;
using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

/// <summary>A planned survivor/source merge with its frozen plan hash and lifecycle. Decision: P1-E03-D11.</summary>
public sealed class MergeRequestRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string MergeRequestId { get; set; } = string.Empty;

    public string SurvivorCustomerId { get; set; } = string.Empty;

    public string SourceCustomerId { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string PlanHash { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string RequiredConsumers { get; set; } = string.Empty;

    public string? ApprovalReference { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<MergeConsumerStatusRecord> Acknowledgements { get; set; } = [];
}

/// <summary>One consumer acknowledgement of a merge. Idempotent per consumer.</summary>
public sealed class MergeConsumerStatusRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string MergeRequestId { get; set; } = string.Empty;

    public string Consumer { get; set; } = string.Empty;

    public DateTimeOffset AcknowledgedAt { get; set; }

    public MergeRequestRecord? MergeRequest { get; set; }
}

/// <summary>
/// Permanent redirect from a merged-away customer to its survivor. Alias identifiers are never reused.
/// Decision: P1-E03-D11.
/// </summary>
public sealed class CustomerAliasRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string AliasCustomerId { get; set; } = string.Empty;

    public string SurvivorCustomerId { get; set; } = string.Empty;

    public string MergeRequestId { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
