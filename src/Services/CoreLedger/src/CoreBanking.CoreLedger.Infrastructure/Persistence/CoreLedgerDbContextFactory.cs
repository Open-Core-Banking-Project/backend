using System;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

/// <summary>Creates the Core Ledger model for migration tooling without establishing a tenant.</summary>
public sealed class CoreLedgerDbContextFactory : IDesignTimeDbContextFactory<CoreLedgerDbContext>
{
    public const string ConnectionStringVariableName = "ConnectionStrings__CoreLedger";

    private const string ModelOnlyConnectionString =
        "Host=127.0.0.1;Port=5432;Database=core_ledger_db;Username=coreledger_migrator;Password=model-only";

    public CoreLedgerDbContext CreateDbContext(string[] args)
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        string connectionString = string.IsNullOrWhiteSpace(configured) ? ModelOnlyConnectionString : configured;

        DbContextOptionsBuilder<CoreLedgerDbContext> options = new();
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly(typeof(CoreLedgerDbContext).Assembly.FullName));

        return new CoreLedgerDbContext(options.Options, new DesignTimeTenantContext());
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
