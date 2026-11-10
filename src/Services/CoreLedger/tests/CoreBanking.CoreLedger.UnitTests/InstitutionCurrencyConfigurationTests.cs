using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Infrastructure.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

/// <summary>
/// The currency configuration area is authored only through the approved lifecycle: tenant-scoped,
/// validated, exactly simulated, approved by Workflow and published by someone other than the maker.
/// Decision: P1-E04-D17.
/// </summary>
public sealed class InstitutionCurrencyConfigurationTests
{
    private static readonly InstitutionId Institution = InstitutionId.From("institution-1");
    private static readonly ActorId Actor = ActorId.From("configuration-maker");

    [Fact]
    public async Task A_cross_tenant_target_is_refused_before_anything_is_written()
    {
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(Institution);
        await using CoreLedgerDbContext context = CreateContext(tenantContext);
        InstitutionCurrencyConfigurationService service = CreateService(context, tenantContext);

        InstitutionCurrencyConfigurationRejectedException exception = await Assert.ThrowsAsync<InstitutionCurrencyConfigurationRejectedException>(
            () => service.CreateDraftAsync(
                new CreateCurrencyDraftCommand(
                    InstitutionId.From("institution-2"),
                    Actor,
                    "idempotency-1",
                    "version-1",
                    "NGN",
                    ["NGN"],
                    "correlation-1"),
                CancellationToken.None));

        Assert.Equal("cross_tenant_access_denied", exception.Code);
    }

    [Fact]
    public async Task A_set_without_its_base_currency_is_refused()
    {
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(Institution);
        await using CoreLedgerDbContext context = CreateContext(tenantContext);
        InstitutionCurrencyConfigurationService service = CreateService(context, tenantContext);

        InstitutionCurrencyConfigurationRejectedException exception = await Assert.ThrowsAsync<InstitutionCurrencyConfigurationRejectedException>(
            () => service.CreateDraftAsync(
                new CreateCurrencyDraftCommand(
                    Institution,
                    Actor,
                    "idempotency-1",
                    "version-1",
                    "NGN",
                    ["USD"],
                    "correlation-1"),
                CancellationToken.None));

        Assert.Equal("currency_configuration_invalid", exception.Code);
    }

    [Fact]
    public async Task A_non_iso_code_is_refused()
    {
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(Institution);
        await using CoreLedgerDbContext context = CreateContext(tenantContext);
        InstitutionCurrencyConfigurationService service = CreateService(context, tenantContext);

        InstitutionCurrencyConfigurationRejectedException exception = await Assert.ThrowsAsync<InstitutionCurrencyConfigurationRejectedException>(
            () => service.CreateDraftAsync(
                new CreateCurrencyDraftCommand(
                    Institution,
                    Actor,
                    "idempotency-1",
                    "version-1",
                    "N1N",
                    ["N1N"],
                    "correlation-1"),
                CancellationToken.None));

        Assert.Equal("currency_configuration_invalid", exception.Code);
    }

    [Fact]
    public void The_source_contract_pins_the_approved_profile()
    {
        Assert.Equal("institution.configuration.publish.v1", InstitutionConfigurationApprovalV1.WorkflowKey);
        Assert.Equal("institution-configuration-publication-v1", InstitutionConfigurationApprovalV1.WorkflowVersionId);
        Assert.Equal("institution.configuration.version", InstitutionConfigurationApprovalV1.SubjectType);
        Assert.Equal("core-ledger", InstitutionConfigurationApprovalV1.SourceService);
        Assert.Equal("institution.configuration.approve", InstitutionConfigurationApprovalV1.RequiredPermission);
        Assert.Equal(
            "9438680d220904f00a9e0d18f3fce2c2e4408a1a8a4800648dceff5f2e6c686a",
            InstitutionConfigurationApprovalV1.WorkflowDefinitionHash);
    }

    private static InstitutionCurrencyConfigurationService CreateService(
        CoreLedgerDbContext context,
        AmbientTenantContext tenantContext) =>
        new(context, new IdempotencyService(context, TimeProvider.System), tenantContext, TimeProvider.System);

    private static CoreLedgerDbContext CreateContext(AmbientTenantContext tenantContext)
    {
        DbContextOptions<CoreLedgerDbContext> options = new DbContextOptionsBuilder<CoreLedgerDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;******")
            .Options;
        return new CoreLedgerDbContext(options, tenantContext);
    }
}
