using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Platform;

/// <summary>
/// A synthetic, non-financial record used to prove the Phase 0 messaging flow: it is written in the
/// same transaction as its outbox record, and a consumer observes the event that describes it.
/// Table: <c>platform.foundation_records</c>. Decision: epic 04 reference flow.
/// </summary>
public sealed class FoundationRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string FoundationRecordId { get; set; }

    public required string Description { get; set; }

    public required string CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
