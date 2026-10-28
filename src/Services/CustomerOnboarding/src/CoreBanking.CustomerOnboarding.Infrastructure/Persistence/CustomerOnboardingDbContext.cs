using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

/// <summary>Customer Onboarding's independently owned, fail-closed persistence boundary.</summary>
public sealed class CustomerOnboardingDbContext : DbContext, IIntegrationStore
{
    private readonly ITenantContext _tenantContext;

    public CustomerOnboardingDbContext(
        DbContextOptions<CustomerOnboardingDbContext> options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext ?? throw new System.ArgumentNullException(nameof(tenantContext));
    }

    public DbSet<InstitutionBoundaryRecord> InstitutionBoundaries => Set<InstitutionBoundaryRecord>();

    public DbSet<PartyRecord> Parties => Set<PartyRecord>();

    public DbSet<PartyIndividualRecord> PartyIndividuals => Set<PartyIndividualRecord>();

    public DbSet<PartyNameRecord> PartyNames => Set<PartyNameRecord>();

    public DbSet<PartyContactRecord> PartyContacts => Set<PartyContactRecord>();

    public DbSet<PartyAddressRecord> PartyAddresses => Set<PartyAddressRecord>();

    public DbSet<OnboardingApplicationRecord> OnboardingApplications => Set<OnboardingApplicationRecord>();

    public DbSet<OnboardingApplicationRevisionRecord> OnboardingApplicationRevisions => Set<OnboardingApplicationRevisionRecord>();

    public DbSet<OutboxRecord> OutboxMessages => Set<OutboxRecord>();

    public DbSet<InboxRecord> InboxMessages => Set<InboxRecord>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<DeadLetterRecord> DeadLetters => Set<DeadLetterRecord>();

    internal string? CurrentInstitutionId => _tenantContext.CurrentInstitution is InstitutionId current
        ? current.Value
        : null;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        System.ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyIntegrationRecords();
        modelBuilder.ApplyPartyRecords();
        modelBuilder.ApplyOnboardingRecords();

        modelBuilder.Entity<InstitutionBoundaryRecord>(entity =>
        {
            entity.ToTable("institution_boundaries", "platform", table =>
            {
                table.HasCheckConstraint("ck_institution_boundaries_plan_version", "provisioning_plan_version > 0");
                table.HasCheckConstraint("ck_institution_boundaries_status", "status IN ('provisioning', 'active', 'suspended')");
            });
            entity.HasKey(record => record.InstitutionId);
            entity.HasIndex(record => record.SourceEventId).IsUnique();
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.ProvisioningPlanVersion).HasColumnName("provisioning_plan_version");
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.SourceEventId).HasColumnName("source_event_id").HasMaxLength(128);
            entity.Property(record => record.InitialisedAt).HasColumnName("initialised_at");
        });

        modelBuilder.Entity<InstitutionBoundaryRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<OutboxRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<InboxRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<IdempotencyRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<DeadLetterRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<PartyRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<PartyIndividualRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<PartyNameRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<PartyContactRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<PartyAddressRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<OnboardingApplicationRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<OnboardingApplicationRevisionRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);
}
