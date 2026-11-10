using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using CoreBanking.BuildingBlocks.Application.Diagnostics;
using CoreBanking.IdentityAccess.Infrastructure.Observability;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Observability;

[Trait("Category", "Unit")]
public sealed class IdentityAccessMetricsTests
{
    [Fact]
    public void Reconciliation_instruments_are_bounded_and_published_on_the_exported_meter()
    {
        List<string> observed = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (string.Equals(instrument.Meter.Name, CoreBankingMeterNames.IdentityAccess, StringComparison.Ordinal))
            {
                observed.Add(instrument.Name);
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => { });
        listener.Start();

        IdentityAccessMetrics.GrantHoldersWithoutVersion.Add(1);
        IdentityAccessMetrics.ExpiredGrantsNotClosed.Add(1);
        IdentityAccessMetrics.ExpiredServiceClientsActive.Add(1);
        IdentityAccessMetrics.StaleStepUpEvidence.Add(1);

        Assert.Equal(
            [
                "identity.reconciliation.expired_grants_not_closed",
                "identity.reconciliation.expired_service_clients_active",
                "identity.reconciliation.grant_holders_without_version",
                "identity.reconciliation.stale_step_up_evidence",
            ],
            observed.Order(StringComparer.Ordinal));
    }
}
