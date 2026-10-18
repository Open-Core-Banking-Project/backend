using System;
using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Institutions;

namespace CoreBanking.CoreLedger.Application.Provisioning;

public sealed class TenantProvisioningPlanOptions
{
    public const string SectionName = "InstitutionProvisioning";

    public int PlanVersion { get; set; }

    public IReadOnlyList<string> RequiredServices { get; init; } = Array.Empty<string>();

    public void Validate() => TenantProvisioning.ValidatePlan(PlanVersion, RequiredServices);

    public TenantProvisioning CreateFor(InstitutionId institutionId) =>
        TenantProvisioning.Create(institutionId, PlanVersion, RequiredServices);
}
