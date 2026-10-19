using System;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoreBanking.RiskReporting.Infrastructure.Persistence;

public sealed class RiskReportingDbContextFactory : IDesignTimeDbContextFactory<RiskReportingDbContext>
{
    private const string ModelOnlyConnectionString =
        "Host=127.0.0.1;Port=5432;Database=risk_reporting_db;Username=riskreporting_migrator;Password=model-only";

    public RiskReportingDbContext CreateDbContext(string[] args)
    {
        string? configured = Environment.GetEnvironmentVariable("ConnectionStrings__RiskReporting");
        string connectionString = string.IsNullOrWhiteSpace(configured) ? ModelOnlyConnectionString : configured;
        DbContextOptionsBuilder<RiskReportingDbContext> options = new();
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(RiskReportingDbContext).Assembly.FullName));
        return new RiskReportingDbContext(options.Options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public InstitutionId? CurrentInstitution => null;
        public void Establish(InstitutionId institutionId) => throw new NotSupportedException("The design-time context has no institution.");
        public void Clear() { }
    }
}
