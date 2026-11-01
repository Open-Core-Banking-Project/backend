using System;
using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

/// <summary>The institution banking relationship. Decision: P1-E03-D09, P1-E03-D10.</summary>
public sealed class CustomerRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string CustomerId { get; set; } = string.Empty;

    public string PartyId { get; set; } = string.Empty;

    public string CustomerNumber { get; set; } = string.Empty;

    public string? BranchId { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long ConcurrencyVersion { get; set; }

    public ICollection<CustomerClassificationRecord> Classifications { get; set; } = [];

    public ICollection<CustomerStatusHistoryRecord> StatusHistory { get; set; } = [];
}

/// <summary>A versioned tier, segment, risk class and EDD flag with its approval evidence.</summary>
public sealed class CustomerClassificationRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string CustomerId { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Tier { get; set; } = string.Empty;

    public string Segment { get; set; } = string.Empty;

    public string RiskClass { get; set; } = string.Empty;

    public bool RequiresEnhancedDueDiligence { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? WorkflowDecisionReference { get; set; }

    public string? ApprovalRequestId { get; set; }

    public string? ApprovalContentHash { get; set; }

    public DateTimeOffset? SubmittedForApprovalAt { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset ProposedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public CustomerRecord? Customer { get; set; }
}

/// <summary>Append-only status transitions with their reason and approval reference.</summary>
public sealed class CustomerStatusHistoryRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string CustomerId { get; set; } = string.Empty;

    public int Sequence { get; set; }

    public string FromStatus { get; set; } = string.Empty;

    public string ToStatus { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string ActorId { get; set; } = string.Empty;

    public string? ApprovalReference { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public CustomerRecord? Customer { get; set; }
}

/// <summary>
/// The institution-scoped customer number sequence. Numbers are monotonic and never reused; a failed create
/// can leave a gap, which is acceptable because the contract is uniqueness and non-reuse, not contiguity.
/// Decision: P1-E03-D02.
/// </summary>
public sealed class CustomerNumberSequenceRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public long NextValue { get; set; }
}
