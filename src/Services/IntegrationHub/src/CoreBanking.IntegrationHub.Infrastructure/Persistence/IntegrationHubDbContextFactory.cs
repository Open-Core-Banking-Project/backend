using System;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoreBanking.IntegrationHub.Infrastructure.Persistence;

public sealed class IntegrationHubDbContextFactory : IDesignTimeDbContextFactory<IntegrationHubDbContext>
{
    private const string ModelOnlyConnectionString =
        "Host=127.0.0.1;Port=5432;Database=integration_hub_db;Username=integrationhub_migrator;Password=model-only";

    public IntegrationHubDbContext CreateDbContext(string[] args)
    {
        string? configured = Environment.GetEnvironmentVariable("ConnectionStrings__IntegrationHub");
        string connectionString = string.IsNullOrWhiteSpace(configured) ? ModelOnlyConnectionString : configured;
        DbContextOptionsBuilder<IntegrationHubDbContext> options = new();
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(IntegrationHubDbContext).Assembly.FullName));
        return new IntegrationHubDbContext(options.Options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public InstitutionId? CurrentInstitution => null;
        public void Establish(InstitutionId institutionId) => throw new NotSupportedException("The design-time context has no institution.");
        public void Clear() { }
    }
}
