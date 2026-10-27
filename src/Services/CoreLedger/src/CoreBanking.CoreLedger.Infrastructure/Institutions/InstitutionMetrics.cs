using System.Diagnostics.Metrics;

namespace CoreBanking.CoreLedger.Infrastructure.Institutions;

/// <summary>Bounded operational signals for institution provisioning and repair.</summary>
public sealed class InstitutionMetrics
{
    public const string MeterName = "CoreBanking.Institutions";

    private InstitutionMetrics(Meter meter)
    {
        Meter = meter;
        ProvisioningRepairRequests = meter.CreateCounter<long>("corebanking.institution.provisioning.repair.requests");
        ProvisioningRepairRejections = meter.CreateCounter<long>("corebanking.institution.provisioning.repair.rejections");
    }

    public static InstitutionMetrics Shared { get; } = new(new Meter(MeterName));
    public Meter Meter { get; }
    public Counter<long> ProvisioningRepairRequests { get; }
    public Counter<long> ProvisioningRepairRejections { get; }
}
