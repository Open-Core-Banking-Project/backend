using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Configurations;

/// <summary>
/// Merge plans, consumer acknowledgements and permanent aliases. A partial unique index allows only one
/// active merge per source customer. Decision: P1-E03-D11.
/// </summary>
public static class MergeRecordConfiguration
{
    public static void ApplyMergeRecords(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new MergeRequestEntityConfiguration());
        modelBuilder.ApplyConfiguration(new MergeConsumerStatusEntityConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerAliasEntityConfiguration());
    }

    private sealed class MergeRequestEntityConfiguration : IEntityTypeConfiguration<MergeRequestRecord>
    {
        public void Configure(EntityTypeBuilder<MergeRequestRecord> entity)
        {
            entity.ToTable("merge_requests", "customer", table =>
            {
                table.HasCheckConstraint(
                    "ck_merge_requests_status",
                    "status IN ('proposed', 'analysed', 'pending_approval', 'approved', 'applying', 'completed', 'reconciliation_required', 'rejected', 'withdrawn')");
                table.HasCheckConstraint("ck_merge_requests_distinct", "survivor_customer_id <> source_customer_id");
                table.HasCheckConstraint("ck_merge_requests_plan_hash", "char_length(plan_hash) = 64");
                table.HasCheckConstraint(
                    "ck_merge_requests_approved",
                    "status NOT IN ('approved', 'applying', 'completed', 'reconciliation_required') OR approval_reference IS NOT NULL");
            });

            entity.HasKey(record => new { record.InstitutionId, record.MergeRequestId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.MergeRequestId).HasColumnName("merge_request_id").HasMaxLength(64);
            entity.Property(record => record.SurvivorCustomerId).HasColumnName("survivor_customer_id").HasMaxLength(64);
            entity.Property(record => record.SourceCustomerId).HasColumnName("source_customer_id").HasMaxLength(64);
            entity.Property(record => record.Reason).HasColumnName("reason").HasMaxLength(500);
            entity.Property(record => record.PlanHash).HasColumnName("plan_hash").HasMaxLength(64);
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.RequiredConsumers).HasColumnName("required_consumers").HasColumnType("jsonb");
            entity.Property(record => record.ApprovalReference).HasColumnName("approval_reference").HasMaxLength(128);
            entity.Property(record => record.CreatedAt).HasColumnName("created_at");
            entity.Property(record => record.UpdatedAt).HasColumnName("updated_at");
            entity.Property(record => record.CompletedAt).HasColumnName("completed_at");

            entity.HasIndex(record => new { record.InstitutionId, record.SourceCustomerId })
                .HasDatabaseName("ux_merge_requests_active_source")
                .HasFilter("status NOT IN ('completed', 'rejected', 'withdrawn')")
                .IsUnique();
        }
    }

    private sealed class MergeConsumerStatusEntityConfiguration : IEntityTypeConfiguration<MergeConsumerStatusRecord>
    {
        public void Configure(EntityTypeBuilder<MergeConsumerStatusRecord> entity)
        {
            entity.ToTable("merge_consumer_status", "customer");

            entity.HasKey(record => new { record.InstitutionId, record.MergeRequestId, record.Consumer });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.MergeRequestId).HasColumnName("merge_request_id").HasMaxLength(64);
            entity.Property(record => record.Consumer).HasColumnName("consumer").HasMaxLength(64);
            entity.Property(record => record.AcknowledgedAt).HasColumnName("acknowledged_at");

            entity.HasOne(record => record.MergeRequest)
                .WithMany(request => request.Acknowledgements)
                .HasForeignKey(record => new { record.InstitutionId, record.MergeRequestId })
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    private sealed class CustomerAliasEntityConfiguration : IEntityTypeConfiguration<CustomerAliasRecord>
    {
        public void Configure(EntityTypeBuilder<CustomerAliasRecord> entity)
        {
            entity.ToTable("customer_aliases", "customer", table =>
                table.HasCheckConstraint("ck_customer_aliases_distinct", "alias_customer_id <> survivor_customer_id"));

            entity.HasKey(record => new { record.InstitutionId, record.AliasCustomerId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.AliasCustomerId).HasColumnName("alias_customer_id").HasMaxLength(64);
            entity.Property(record => record.SurvivorCustomerId).HasColumnName("survivor_customer_id").HasMaxLength(64);
            entity.Property(record => record.MergeRequestId).HasColumnName("merge_request_id").HasMaxLength(64);
            entity.Property(record => record.Reason).HasColumnName("reason").HasMaxLength(500);
            entity.Property(record => record.CreatedBy).HasColumnName("created_by").HasMaxLength(128);
            entity.Property(record => record.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(record => new { record.InstitutionId, record.SurvivorCustomerId })
                .HasDatabaseName("ix_customer_aliases_survivor");
        }
    }
}
