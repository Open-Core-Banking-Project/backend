using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

/// <summary>The authoritative institution identity and lifecycle persisted by Core Ledger.</summary>
public sealed class InstitutionRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string Code { get; set; }

    public required string LegalName { get; set; }

    public required string CountryCode { get; set; }

    public required string BaseCurrency { get; set; }

    public required string Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ProvisioningStartedAt { get; set; }

    public DateTimeOffset? ProvisioningFailedAt { get; set; }

    public DateTimeOffset? LastProvisioningAttemptAt { get; set; }

    public int? ProvisioningPlanVersion { get; set; }

    public DateTimeOffset? ReadyForActivationAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? SuspendedAt { get; set; }
}
