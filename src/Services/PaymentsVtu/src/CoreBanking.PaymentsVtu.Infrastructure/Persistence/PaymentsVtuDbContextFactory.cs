using System;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoreBanking.PaymentsVtu.Infrastructure.Persistence;

public sealed class PaymentsVtuDbContextFactory : IDesignTimeDbContextFactory<PaymentsVtuDbContext>
{
    private const string ModelOnlyConnectionString =
        "Host=127.0.0.1;Port=5432;Database=payments_vtu_db;Username=paymentsvtu_migrator;Password=model-only";

    public PaymentsVtuDbContext CreateDbContext(string[] args)
    {
        string? configured = Environment.GetEnvironmentVariable("ConnectionStrings__PaymentsVtu");
        string connectionString = string.IsNullOrWhiteSpace(configured) ? ModelOnlyConnectionString : configured;
        DbContextOptionsBuilder<PaymentsVtuDbContext> options = new();
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(PaymentsVtuDbContext).Assembly.FullName));
        return new PaymentsVtuDbContext(options.Options, new DesignTimeTenantContext());
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
