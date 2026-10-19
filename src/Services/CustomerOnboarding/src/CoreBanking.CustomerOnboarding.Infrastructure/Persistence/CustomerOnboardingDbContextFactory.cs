using System;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

public sealed class CustomerOnboardingDbContextFactory : IDesignTimeDbContextFactory<CustomerOnboardingDbContext>
{
    private const string ModelOnlyConnectionString =
        "Host=127.0.0.1;Port=5432;Database=customer_onboarding_db;Username=customeronboarding_migrator;Password=model-only";

    public CustomerOnboardingDbContext CreateDbContext(string[] args)
    {
        string? configured = Environment.GetEnvironmentVariable("ConnectionStrings__CustomerOnboarding");
        string connectionString = string.IsNullOrWhiteSpace(configured) ? ModelOnlyConnectionString : configured;
        DbContextOptionsBuilder<CustomerOnboardingDbContext> options = new();
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly(typeof(CustomerOnboardingDbContext).Assembly.FullName));
        return new CustomerOnboardingDbContext(options.Options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public InstitutionId? CurrentInstitution => null;

        public void Establish(InstitutionId institutionId) =>
            throw new NotSupportedException("The design-time context has no institution.");

        public void Clear()
        {
        }
    }
}
