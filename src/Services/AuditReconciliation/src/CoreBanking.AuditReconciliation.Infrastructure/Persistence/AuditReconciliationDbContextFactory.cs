using System;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoreBanking.AuditReconciliation.Infrastructure.Persistence;

/// <summary>Creates the Audit Reconciliation model for EF tooling without connecting by default.</summary>
public sealed class AuditReconciliationDbContextFactory : IDesignTimeDbContextFactory<AuditReconciliationDbContext>
{
    private const string ModelOnlyConnectionString =
        "Host=127.0.0.1;Port=5432;Database=audit_reconciliation_db;Username=auditreconciliation_migrator;Password=model-only";

    public AuditReconciliationDbContext CreateDbContext(string[] args)
    {
        string? configured = Environment.GetEnvironmentVariable("ConnectionStrings__AuditReconciliation");
        string connectionString = string.IsNullOrWhiteSpace(configured) ? ModelOnlyConnectionString : configured;

        DbContextOptionsBuilder<AuditReconciliationDbContext> options = new();
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AuditReconciliationDbContext).Assembly.FullName));

        return new AuditReconciliationDbContext(options.Options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public InstitutionId? CurrentInstitution => null;

        public void Establish(InstitutionId institutionId) =>
            throw new System.NotSupportedException("The design-time context has no institution.");

        public void Clear()
        {
        }
    }
}
