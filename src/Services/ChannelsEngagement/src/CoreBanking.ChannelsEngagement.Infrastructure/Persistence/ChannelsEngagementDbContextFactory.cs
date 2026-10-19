using System;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoreBanking.ChannelsEngagement.Infrastructure.Persistence;

public sealed class ChannelsEngagementDbContextFactory : IDesignTimeDbContextFactory<ChannelsEngagementDbContext>
{
    private const string ModelOnlyConnectionString =
        "Host=127.0.0.1;Port=5432;Database=channels_engagement_db;Username=channelsengagement_migrator;Password=model-only";

    public ChannelsEngagementDbContext CreateDbContext(string[] args)
    {
        string? configured = Environment.GetEnvironmentVariable("ConnectionStrings__ChannelsEngagement");
        string connectionString = string.IsNullOrWhiteSpace(configured) ? ModelOnlyConnectionString : configured;
        DbContextOptionsBuilder<ChannelsEngagementDbContext> options = new();
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(ChannelsEngagementDbContext).Assembly.FullName));
        return new ChannelsEngagementDbContext(options.Options, new DesignTimeTenantContext());
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
