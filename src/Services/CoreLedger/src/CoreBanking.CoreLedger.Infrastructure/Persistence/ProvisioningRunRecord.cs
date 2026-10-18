using System;
using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

/// <summary>A durable, versioned attempt to initialise every mandatory service boundary.</summary>
public sealed class ProvisioningRunRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string ProvisioningRunId { get; set; }

    public int PlanVersion { get; set; }

    public required string Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<ProvisioningStepRecord> Steps { get; } = new();
}
