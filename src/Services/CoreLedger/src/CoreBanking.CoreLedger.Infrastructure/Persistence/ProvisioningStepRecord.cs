using System;
using CoreBanking.BuildingBlocks.Application.Persistence;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

/// <summary>The idempotent acknowledgement state of one service in one provisioning run.</summary>
public sealed class ProvisioningStepRecord : ITenantOwnedEntity
{
    public required string InstitutionId { get; set; }

    public required string ProvisioningRunId { get; set; }

    public required string ServiceId { get; set; }

    public required string Status { get; set; }

    public string? FailureCode { get; set; }

    public string? AcknowledgementEventId { get; set; }

    public DateTimeOffset? AcknowledgedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ProvisioningRunRecord? Run { get; set; }
}
