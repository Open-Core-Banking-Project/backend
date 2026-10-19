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

public sealed class InstitutionSuspensionServiceTests
{
    [Fact]
    public void Actor_that_last_activated_cannot_approve_suspension()
    {
        InstitutionSuspensionRejectedException exception = Assert.Throws<InstitutionSuspensionRejectedException>(() =>
            InstitutionSuspensionPolicy.EnsureIndependentApprover("actor-1", ActorId.From("actor-1")));

        Assert.Equal("maker_cannot_approve_own_change", exception.Code);
    }

    [Fact]
    public async System.Threading.Tasks.Task Suspension_fails_closed_for_a_different_trusted_institution()
    {
        AmbientTenantContext tenant = new();
        tenant.Establish(InstitutionId.From("institution-trusted"));
        DbContextOptions<CoreLedgerDbContext> options = new DbContextOptionsBuilder<CoreLedgerDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;Password=model").Options;
        await using CoreLedgerDbContext context = new(options, tenant);
        InstitutionSuspensionService service = new(context, new IdempotencyService(context, TimeProvider.System), tenant, TimeProvider.System);

        InstitutionSuspensionRejectedException exception = await Assert.ThrowsAsync<InstitutionSuspensionRejectedException>(() =>
            service.SuspendAsync(new SuspendInstitutionCommand(
                InstitutionId.From("institution-other"), ActorId.From("approver"), "key", "Operational incident", "correlation"), CancellationToken.None));

        Assert.Equal("cross_tenant_access_denied", exception.Code);
    }
}
