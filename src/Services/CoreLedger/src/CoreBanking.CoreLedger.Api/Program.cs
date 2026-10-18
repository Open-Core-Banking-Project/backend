using System;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Institutions;
using CoreBanking.CoreLedger.Application.Provisioning;
using CoreBanking.CoreLedger.Infrastructure.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
Microsoft.Extensions.Configuration.IConfigurationSection provisioningSection =
    builder.Configuration.GetSection(TenantProvisioningPlanOptions.SectionName);
string? configuredPlanVersion = provisioningSection["PlanVersion"];
if (!int.TryParse(configuredPlanVersion, NumberStyles.None, CultureInfo.InvariantCulture, out int planVersion))
{
    throw new InvalidOperationException("Institution provisioning plan version must be a positive integer.");
}

string[] requiredServices = provisioningSection
    .GetSection("RequiredServices")
    .GetChildren()
    .Select(service => service.Value
        ?? throw new InvalidOperationException("Every required provisioning service must have an identifier."))
    .ToArray();
TenantProvisioningPlanOptions provisioningPlan = new()
{
    PlanVersion = planVersion,
    RequiredServices = requiredServices,
};
provisioningPlan.Validate();
builder.Services.AddSingleton(provisioningPlan);
builder.Services.AddSingleton<ITenantContext, AmbientTenantContext>();
builder.Services.AddSingleton(TimeProvider.System);

string? connectionString = builder.Configuration.GetConnectionString("CoreLedger");
bool useRelationalStore = !string.IsNullOrWhiteSpace(connectionString);
if (!useRelationalStore && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("ConnectionStrings:CoreLedger is required outside Development.");
}

if (useRelationalStore)
{
    builder.Services.AddDbContext<CoreLedgerDbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddScoped<IIntegrationStore>(provider => provider.GetRequiredService<CoreLedgerDbContext>());
    builder.Services.AddScoped<IdempotencyService>();
    builder.Services.AddScoped<IInstitutionCreationService, InstitutionCreationService>();
}

CoreBanking.BuildingBlocks.Observability.CoreBankingObservability.AddCoreBankingObservability(
    builder.Services,
    builder.Configuration,
    builder.Environment,
    "corebanking.core-ledger.api");
WebApplication app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", async (CancellationToken cancellationToken) =>
{
    if (!useRelationalStore)
    {
        return Results.Ok(new { status = "Healthy" });
    }

    try
    {
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        CoreLedgerDbContext context = scope.ServiceProvider.GetRequiredService<CoreLedgerDbContext>();
        return await context.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "Healthy" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch (DbException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();
