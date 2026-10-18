using System;
using System.Threading;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Application.Provisioning;
using CoreBanking.CoreLedger.Infrastructure.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class InstitutionCreationServiceTests
{
    [Fact]
    public async System.Threading.Tasks.Task Creation_fails_closed_when_trusted_context_does_not_match_the_target()
    {
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(InstitutionId.From("institution-trusted"));
        await using CoreLedgerDbContext context = CreateContext(tenantContext);
        InstitutionCreationService service = new(
            context,
            new IdempotencyService(context, TimeProvider.System),
            tenantContext,
            new TenantProvisioningPlanOptions
            {
                PlanVersion = 1,
                RequiredServices = ["core-ledger", "identity-access"],
            },
            TimeProvider.System);
        CreateInstitutionCommand command = new(
            InstitutionId.From("institution-untrusted"),
            ActorId.From("platform-admin"),
            "idempotency-key",
            "bank-001",
            "Bank One",
            "NG",
            "NGN",
            "correlation-001");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(command, CancellationToken.None));

        Assert.Contains("trusted context", exception.Message, StringComparison.Ordinal);
    }

    private static CoreLedgerDbContext CreateContext(AmbientTenantContext tenantContext)
    {
        DbContextOptions<CoreLedgerDbContext> options = new DbContextOptionsBuilder<CoreLedgerDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;Password=model")
            .Options;

        return new CoreLedgerDbContext(options, tenantContext);
    }
}
