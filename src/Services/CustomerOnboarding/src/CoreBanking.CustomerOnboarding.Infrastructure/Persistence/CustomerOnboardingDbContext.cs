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

    public DbSet<DuplicateCandidateSetRecord> DuplicateCandidateSets => Set<DuplicateCandidateSetRecord>();

    public DbSet<DuplicateCandidateRecord> DuplicateCandidates => Set<DuplicateCandidateRecord>();

    public DbSet<RequirementPackRecord> RequirementPacks => Set<RequirementPackRecord>();

    public DbSet<RequirementSnapshotRecord> RequirementSnapshots => Set<RequirementSnapshotRecord>();

    public DbSet<DocumentReferenceRecord> DocumentReferences => Set<DocumentReferenceRecord>();

    public DbSet<KycCaseRecord> KycCases => Set<KycCaseRecord>();

    public DbSet<KycRequirementRecord> KycRequirements => Set<KycRequirementRecord>();

    public DbSet<IdentityVerificationRecord> IdentityVerifications => Set<IdentityVerificationRecord>();

    public DbSet<VerificationAttemptRecord> VerificationAttempts => Set<VerificationAttemptRecord>();

    public DbSet<VerificationStatusHistoryRecord> VerificationStatusHistory => Set<VerificationStatusHistoryRecord>();

    public DbSet<KycRequirementHistoryRecord> KycRequirementHistory => Set<KycRequirementHistoryRecord>();

    public DbSet<CustomerRecord> Customers => Set<CustomerRecord>();

    public DbSet<CustomerClassificationRecord> CustomerClassifications => Set<CustomerClassificationRecord>();

    public DbSet<CustomerStatusHistoryRecord> CustomerStatusHistory => Set<CustomerStatusHistoryRecord>();

    public DbSet<CustomerNumberSequenceRecord> CustomerNumberSequences => Set<CustomerNumberSequenceRecord>();

    public DbSet<ServicePreferenceRecord> ServicePreferences => Set<ServicePreferenceRecord>();

    public DbSet<ConsentEvidenceRecord> ConsentEvidence => Set<ConsentEvidenceRecord>();

    public DbSet<RelationshipManagerAssignmentRecord> RelationshipManagerAssignments => Set<RelationshipManagerAssignmentRecord>();

    public DbSet<CustomerCorrectionRecord> CustomerCorrections => Set<CustomerCorrectionRecord>();

    public DbSet<MergeRequestRecord> MergeRequests => Set<MergeRequestRecord>();

    public DbSet<MergeConsumerStatusRecord> MergeConsumerStatus => Set<MergeConsumerStatusRecord>();

    public DbSet<CustomerAliasRecord> CustomerAliases => Set<CustomerAliasRecord>();

    public DbSet<CustomerAuditEventRecord> CustomerAuditEvents => Set<CustomerAuditEventRecord>();

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
        modelBuilder.ApplyDuplicateRecords();
        modelBuilder.ApplyRequirementRecords();
        modelBuilder.ApplyKycRecords();
        modelBuilder.ApplyCustomerRecords();
        modelBuilder.ApplyPreferenceRecords();
        modelBuilder.ApplyRelationshipManagerRecords();
        modelBuilder.ApplyCorrectionRecords();
        modelBuilder.ApplyMergeRecords();
        modelBuilder.ApplyAuditRecords();

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
        modelBuilder.Entity<DuplicateCandidateSetRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<DuplicateCandidateRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<RequirementPackRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<RequirementSnapshotRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<DocumentReferenceRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<KycCaseRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<KycRequirementRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<IdentityVerificationRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<VerificationAttemptRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<VerificationStatusHistoryRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<KycRequirementHistoryRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<CustomerRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<CustomerClassificationRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<CustomerStatusHistoryRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<CustomerNumberSequenceRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<ServicePreferenceRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<ConsentEvidenceRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<RelationshipManagerAssignmentRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<CustomerCorrectionRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<MergeRequestRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<MergeConsumerStatusRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<CustomerAliasRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<CustomerAuditEventRecord>().HasQueryFilter(record =>
            CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);
}
