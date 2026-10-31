using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

/// <summary>
/// One append-only protected-data correction: the affected field code, the superseded and the new protected
/// value references, the reason and whether evidence must be repeated. Decision: epic 03 CUS-UC-06.
/// </summary>
public sealed class CustomerCorrectionRecord : ITenantOwnedEntity
{
    public string InstitutionId { get; set; } = string.Empty;

    public string CorrectionId { get; set; } = string.Empty;

    public string CustomerId { get; set; } = string.Empty;

    public string FieldCode { get; set; } = string.Empty;

    public string PriorValueReference { get; set; } = string.Empty;

    public string NewValueReference { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string? EvidenceReference { get; set; }

    public bool RequiresReverification { get; set; }

    public string? RequirementCode { get; set; }

    public string? ApprovalReference { get; set; }

    public string RecordedBy { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }

    public CustomerRecord? Customer { get; set; }
}
