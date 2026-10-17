using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CoreBanking.AuditReconciliation.Infrastructure.Persistence;

/// <summary>The locally owned database context for Audit Reconciliation.</summary>
public sealed class AuditReconciliationDbContext : DbContext, IIntegrationStore
{
    private readonly ITenantContext _tenantContext;

    public AuditReconciliationDbContext(
        DbContextOptions<AuditReconciliationDbContext> options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext ?? throw new System.ArgumentNullException(nameof(tenantContext));
    }

    public DbSet<AuditEvidenceRecord> AuditEvidence => Set<AuditEvidenceRecord>();

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
        modelBuilder.ApplyIntegrationRecords();

        modelBuilder.Entity<AuditEvidenceRecord>(entity =>
        {
            entity.ToTable("events", "audit");
            entity.HasKey(record => new { record.InstitutionId, record.AuditEventId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id");
            entity.Property(record => record.AuditEventId).HasColumnName("audit_event_id");
            entity.Property(record => record.ActorId).HasColumnName("actor_id");
            entity.Property(record => record.Action).HasColumnName("action");
            entity.Property(record => record.ResourceType).HasColumnName("resource_type");
            entity.Property(record => record.ResourceId).HasColumnName("resource_id");
            entity.Property(record => record.Decision).HasColumnName("decision");
            entity.Property(record => record.Source).HasColumnName("source");
            entity.Property(record => record.CorrelationId).HasColumnName("correlation_id");
            entity.Property(record => record.OccurredAt).HasColumnName("occurred_at");
        });

        modelBuilder.Entity<AuditEvidenceRecord>()
            .HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<OutboxRecord>()
            .HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<InboxRecord>()
            .HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<IdempotencyRecord>()
            .HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<DeadLetterRecord>()
            .HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);
}
