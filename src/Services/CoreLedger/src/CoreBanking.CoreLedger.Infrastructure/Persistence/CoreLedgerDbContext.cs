using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence;

/// <summary>
/// Core Ledger's authoritative institution persistence boundary. Tenant-owned queries fail closed
/// until trusted infrastructure establishes an institution context (ADR-0004).
/// </summary>
public sealed class CoreLedgerDbContext : DbContext, IIntegrationStore
{
    private readonly ITenantContext _tenantContext;

    public CoreLedgerDbContext(DbContextOptions<CoreLedgerDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext ?? throw new System.ArgumentNullException(nameof(tenantContext));
    }

    public DbSet<InstitutionRecord> Institutions => Set<InstitutionRecord>();

    public DbSet<ProvisioningRunRecord> ProvisioningRuns => Set<ProvisioningRunRecord>();

    public DbSet<ProvisioningStepRecord> ProvisioningSteps => Set<ProvisioningStepRecord>();

    public DbSet<RegionRecord> Regions => Set<RegionRecord>();

    public DbSet<BranchRecord> Branches => Set<BranchRecord>();

    public DbSet<OrganisationStatusHistoryRecord> OrganisationStatusHistory => Set<OrganisationStatusHistoryRecord>();

    public DbSet<BusinessCalendarRecord> BusinessCalendars => Set<BusinessCalendarRecord>();

    public DbSet<CalendarVersionRecord> CalendarVersions => Set<CalendarVersionRecord>();

    public DbSet<CalendarDayRecord> CalendarDays => Set<CalendarDayRecord>();

    public DbSet<BusinessDateControlRecord> BusinessDateControls => Set<BusinessDateControlRecord>();

    public DbSet<BusinessDateAdvanceRequestRecord> BusinessDateAdvanceRequests => Set<BusinessDateAdvanceRequestRecord>();

    public DbSet<BusinessDateHistoryRecord> BusinessDateHistory => Set<BusinessDateHistoryRecord>();

    public DbSet<ConfigurationSetRecord> ConfigurationSets => Set<ConfigurationSetRecord>();

    public DbSet<ConfigurationVersionRecord> ConfigurationVersions => Set<ConfigurationVersionRecord>();

    public DbSet<ConfigurationSimulationRecord> ConfigurationSimulations => Set<ConfigurationSimulationRecord>();

    public DbSet<ConfigurationPublicationRecord> ConfigurationPublications => Set<ConfigurationPublicationRecord>();

    public DbSet<ProductRecord> Products => Set<ProductRecord>();

    public DbSet<InstitutionAuditRecord> InstitutionAuditEvents => Set<InstitutionAuditRecord>();

    public DbSet<OutboxRecord> OutboxMessages => Set<OutboxRecord>();

    public DbSet<InboxRecord> InboxMessages => Set<InboxRecord>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<DeadLetterRecord> DeadLetters => Set<DeadLetterRecord>();

    internal string? CurrentInstitutionId
    {
        get
        {
            InstitutionId? current = _tenantContext.CurrentInstitution;
            return current is null ? null : current.Value;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        System.ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoreLedgerDbContext).Assembly);
        modelBuilder.ApplyIntegrationRecords();
        modelBuilder.Entity<InstitutionRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<ProvisioningRunRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<ProvisioningStepRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<RegionRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<BranchRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<OrganisationStatusHistoryRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<BusinessCalendarRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<CalendarVersionRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<CalendarDayRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<BusinessDateControlRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<BusinessDateAdvanceRequestRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<BusinessDateHistoryRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<ConfigurationSetRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<ConfigurationVersionRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<ConfigurationSimulationRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<ConfigurationPublicationRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<ProductRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<InstitutionAuditRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<OutboxRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<InboxRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<IdempotencyRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<DeadLetterRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditEvidenceIsAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureAuditEvidenceIsAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureAuditEvidenceIsAppendOnly()
    {
        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<InstitutionAuditRecord> entry
                 in ChangeTracker.Entries<InstitutionAuditRecord>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new System.InvalidOperationException("Institution audit evidence is append-only.");
            }
        }
    }
}
