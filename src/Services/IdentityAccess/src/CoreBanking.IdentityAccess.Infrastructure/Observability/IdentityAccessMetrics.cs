using System.Diagnostics.Metrics;
using CoreBanking.BuildingBlocks.Application.Diagnostics;

namespace CoreBanking.IdentityAccess.Infrastructure.Observability;

/// <summary>
/// Bounded, identifier-free measures for identity state that needs attention. No tenant, actor or
/// credential values are exported. Decision: ADR-0010; slice 14.
/// </summary>
public static class IdentityAccessMetrics
{
    public static readonly Meter Meter = new(CoreBankingMeterNames.IdentityAccess, "1.0.0");

    public static readonly Counter<long> GrantHoldersWithoutVersion = Meter.CreateCounter<long>(
        "identity.reconciliation.grant_holders_without_version",
        description: "Actors holding a grant but no positive entitlement version.");

    public static readonly Counter<long> ExpiredGrantsNotClosed = Meter.CreateCounter<long>(
        "identity.reconciliation.expired_grants_not_closed",
        description: "Temporary grants past their end that the sweep has not closed.");

    public static readonly Counter<long> ExpiredServiceClientsActive = Meter.CreateCounter<long>(
        "identity.reconciliation.expired_service_clients_active",
        description: "Service clients still active with an expired credential reference.");

    public static readonly Counter<long> StaleStepUpEvidence = Meter.CreateCounter<long>(
        "identity.reconciliation.stale_step_up_evidence",
        description: "Consumed-or-expired step-up evidence that can no longer authorise an operation.");
}
