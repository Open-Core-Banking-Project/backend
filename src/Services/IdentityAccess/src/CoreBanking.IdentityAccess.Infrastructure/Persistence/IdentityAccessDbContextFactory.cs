using System;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence;

/// <summary>
/// Builds the model for the <c>dotnet ef</c> tools. The fallback connection string exists only
/// so the model can be produced without configuration; it is never used to connect, and the tools
/// are given the real migration principal through the <c>ConnectionStrings__IdentityAccess</c>
/// environment variable. Decision: ADR-0004, ADR-0013.
/// </summary>
public sealed class IdentityAccessDbContextFactory : IDesignTimeDbContextFactory<IdentityAccessDbContext>
{
    public const string ConnectionStringVariableName = "ConnectionStrings__IdentityAccess";

    private const string ModelOnlyConnectionString =
        "Host=127.0.0.1;Port=5432;Database=identity_access_db;Username=identityaccess_migrator;Password=model-only";

    public IdentityAccessDbContext CreateDbContext(string[] args)
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        string connectionString = string.IsNullOrWhiteSpace(configured) ? ModelOnlyConnectionString : configured;

        DbContextOptionsBuilder<IdentityAccessDbContext> options = new();
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(IdentityAccessDbContext).Assembly.FullName));

        return new IdentityAccessDbContext(options.Options, new DesignTimeTenantContext());
    }

    /// <summary>The tools have no request, so no institution is established.</summary>
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public InstitutionId? CurrentInstitution => null;

        public void Establish(InstitutionId institutionId) => throw new NotSupportedException("The design-time context has no institution.");

        public void Clear()
        {
        }
    }
}
