using System;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Provisioning;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class TenantProvisioningPlanOptionsTests
{
    private static readonly string[] RequiredServices =
    [
        "core-ledger",
        "customer-onboarding",
        "identity-access",
        "payments-vtu",
        "channels-engagement",
        "operations-workflow",
        "risk-reporting",
        "integration-hub",
        "audit-reconciliation",
    ];

    [Fact]
    public void Validates_and_creates_the_versioned_configured_plan()
    {
        TenantProvisioningPlanOptions options = new()
        {
            PlanVersion = 1,
            RequiredServices = RequiredServices,
        };

        options.Validate();
        var plan = options.CreateFor(InstitutionId.From("institution-001"));

        Assert.Equal(1, plan.PlanVersion);
        Assert.Equal(9, plan.ServiceStatuses.Count);
    }

    [Fact]
    public void Rejects_an_empty_or_invalid_required_service_list()
    {
        TenantProvisioningPlanOptions options = new()
        {
            PlanVersion = 1,
            RequiredServices = Array.Empty<string>(),
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Plan_creation_copies_and_normalises_the_configured_service_identifiers()
    {
        string[] configuredServices = [" Identity-Access ", "AUDIT-RECONCILIATION"];
        TenantProvisioningPlanOptions options = new()
        {
            PlanVersion = 2,
            RequiredServices = configuredServices,
        };

        var plan = options.CreateFor(InstitutionId.From("institution-001"));
        configuredServices[0] = "payments-vtu";

        Assert.Contains("identity-access", plan.ServiceStatuses.Keys);
        Assert.Contains("audit-reconciliation", plan.ServiceStatuses.Keys);
        Assert.DoesNotContain("payments-vtu", plan.ServiceStatuses.Keys);
    }
}
