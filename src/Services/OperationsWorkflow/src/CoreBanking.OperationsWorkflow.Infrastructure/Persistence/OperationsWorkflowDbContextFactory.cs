using System;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoreBanking.OperationsWorkflow.Infrastructure.Persistence;

public sealed class OperationsWorkflowDbContextFactory : IDesignTimeDbContextFactory<OperationsWorkflowDbContext>
{
    private const string ModelOnlyConnectionString =
        "Host=127.0.0.1;Port=5432;Database=operations_workflow_db;Username=operationsworkflow_migrator;Password=model-only";

    public OperationsWorkflowDbContext CreateDbContext(string[] args)
    {
        string? configured = Environment.GetEnvironmentVariable("ConnectionStrings__OperationsWorkflow");
        string connectionString = string.IsNullOrWhiteSpace(configured) ? ModelOnlyConnectionString : configured;
        DbContextOptionsBuilder<OperationsWorkflowDbContext> options = new();
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(OperationsWorkflowDbContext).Assembly.FullName));
        return new OperationsWorkflowDbContext(options.Options, new DesignTimeTenantContext());
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
