using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace CoreBanking.CustomerOnboarding.Workers;

/// <summary>
/// Operational metrics for customer onboarding reconciliation. Alert on a non-zero finding count per code; the
/// job never repairs state, so an alert means an operator must act. Decision: P1-E03-D14.
/// </summary>
public static class CustomerOnboardingMetrics
{
    public const string MeterName = "CoreBanking.CustomerOnboarding";

    private static readonly Meter Meter = new(MeterName);

    public static Counter<long> ReconciliationFindings { get; } =
        Meter.CreateCounter<long>("customer.reconciliation.findings", unit: "{finding}", description: "Integrity findings by code.");

    public static Counter<long> ReconciliationPasses { get; } =
        Meter.CreateCounter<long>("customer.reconciliation.passes", unit: "{pass}", description: "Completed reconciliation passes.");

    public static void RecordFindings(string code, int count) =>
        ReconciliationFindings.Add(count, new KeyValuePair<string, object?>("code", code));

    public static void RecordPass(bool clean) =>
        ReconciliationPasses.Add(1, new KeyValuePair<string, object?>("clean", clean));
}
