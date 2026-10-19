using System;
using System.Threading;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class InstitutionActivationServiceTests
{
    [Fact]
    public void Maker_cannot_activate_the_institution_they_created()
    {
        InstitutionActivationRejectedException exception = Assert.Throws<InstitutionActivationRejectedException>(() =>
            InstitutionActivationPolicy.EnsureIndependentApprover("maker-1", ActorId.From("maker-1")));

        Assert.Equal("maker_cannot_approve_own_change", exception.Code);
    }

    [Fact]
    public void Activation_requires_every_mandatory_boundary_to_be_initialised()
    {
        InstitutionActivationRejectedException exception = Assert.Throws<InstitutionActivationRejectedException>(() =>
            InstitutionActivationPolicy.EnsureProvisioningComplete(
                "pending_activation",
                ["initialised", "pending"]));

        Assert.Equal("institution_not_provisioned", exception.Code);
    }

    [Fact]
    public void Independent_approver_can_activate_only_a_completed_plan()
    {
        InstitutionActivationPolicy.EnsureIndependentApprover("maker-1", ActorId.From("approver-1"));
        InstitutionActivationPolicy.EnsureProvisioningComplete(
            "pending_activation",
            ["initialised", "initialised"]);
    }

    [Fact]
    public void Suspended_institution_can_be_reactivated_when_boundaries_remain_initialised()
    {
        InstitutionActivationPolicy.EnsureProvisioningComplete("suspended", ["initialised", "initialised"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task Activation_fails_closed_when_trusted_context_does_not_match_target()
    {
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(InstitutionId.From("institution-trusted"));
        await using CoreLedgerDbContext context = CreateContext(tenantContext);
        InstitutionActivationService service = new(
            context,
            new IdempotencyService(context, TimeProvider.System),
            tenantContext,
            TimeProvider.System);

        InstitutionActivationRejectedException exception = await Assert.ThrowsAsync<InstitutionActivationRejectedException>(
            () => service.ActivateAsync(
                new ActivateInstitutionCommand(
                    InstitutionId.From("institution-untrusted"),
                    ActorId.From("approver"),
                    "activation-key",
                    "correlation-1"),
                CancellationToken.None));

        Assert.Equal("cross_tenant_access_denied", exception.Code);
    }

    private static CoreLedgerDbContext CreateContext(AmbientTenantContext tenantContext)
    {
        DbContextOptions<CoreLedgerDbContext> options = new DbContextOptionsBuilder<CoreLedgerDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;Password=model")
            .Options;
        return new CoreLedgerDbContext(options, tenantContext);
    }
}
