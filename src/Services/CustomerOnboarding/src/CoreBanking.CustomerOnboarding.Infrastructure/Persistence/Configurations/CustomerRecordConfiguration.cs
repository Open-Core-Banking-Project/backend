using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Configurations;

/// <summary>
/// The customer relationship tables: one classification version per customer/version, one approved version
/// at a time, an institution-unique customer number and append-only status history.
/// Decision: P1-E03-D09, P1-E03-D10.
/// </summary>
public static class CustomerRecordConfiguration
{
    public static void ApplyCustomerRecords(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new CustomerEntityConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerClassificationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerStatusHistoryEntityConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerNumberSequenceEntityConfiguration());
    }

    private sealed class CustomerEntityConfiguration : IEntityTypeConfiguration<CustomerRecord>
    {
        public void Configure(EntityTypeBuilder<CustomerRecord> entity)
        {
            entity.ToTable("customers", "customer", table =>
            {
                table.HasCheckConstraint(
                    "ck_customers_status",
                    "status IN ('pending', 'active', 'restricted', 'dormant', 'deceased', 'closed', 'rejected')");
                table.HasCheckConstraint("ck_customers_concurrency_version", "concurrency_version > 0");
                table.HasCheckConstraint("ck_customers_customer_number", "char_length(customer_number) > 0");
            });

            entity.HasKey(record => new { record.InstitutionId, record.CustomerId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.CustomerId).HasColumnName("customer_id").HasMaxLength(64);
            entity.Property(record => record.PartyId).HasColumnName("party_id").HasMaxLength(64);
            entity.Property(record => record.CustomerNumber).HasColumnName("customer_number").HasMaxLength(64);
            entity.Property(record => record.BranchId).HasColumnName("branch_id").HasMaxLength(64);
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.CreatedAt).HasColumnName("created_at");
            entity.Property(record => record.UpdatedAt).HasColumnName("updated_at");
            entity.Property(record => record.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();

            entity.HasIndex(record => new { record.InstitutionId, record.CustomerNumber })
                .HasDatabaseName("ux_customers_number")
                .IsUnique();
            entity.HasIndex(record => new { record.InstitutionId, record.PartyId })
                .HasDatabaseName("ux_customers_party")
                .IsUnique();
            entity.HasIndex(record => new { record.InstitutionId, record.Status, record.UpdatedAt })
                .HasDatabaseName("ix_customers_institution_status_updated");

            entity.HasOne<PartyRecord>()
                .WithMany()
                .HasForeignKey(record => new { record.InstitutionId, record.PartyId })
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    private sealed class CustomerClassificationEntityConfiguration : IEntityTypeConfiguration<CustomerClassificationRecord>
    {
        public void Configure(EntityTypeBuilder<CustomerClassificationRecord> entity)
        {
            entity.ToTable("customer_classifications", "customer", table =>
            {
                table.HasCheckConstraint(
                    "ck_customer_classifications_status",
                    "status IN ('proposed', 'approved', 'superseded')");
                table.HasCheckConstraint(
                    "ck_customer_classifications_approval_hash",
                    "approval_content_hash IS NULL OR length(approval_content_hash) = 64");
                table.HasCheckConstraint("ck_customer_classifications_version", "version >= 1");
                table.HasCheckConstraint(
                    "ck_customer_classifications_approved",
                    "status <> 'approved' OR (workflow_decision_reference IS NOT NULL AND approved_at IS NOT NULL)");
            });

            entity.HasKey(record => new { record.InstitutionId, record.CustomerId, record.Version });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.CustomerId).HasColumnName("customer_id").HasMaxLength(64);
            entity.Property(record => record.Version).HasColumnName("version");
            entity.Property(record => record.Tier).HasColumnName("tier").HasMaxLength(64);
            entity.Property(record => record.Segment).HasColumnName("segment").HasMaxLength(64);
            entity.Property(record => record.RiskClass).HasColumnName("risk_class").HasMaxLength(64);
            entity.Property(record => record.RequiresEnhancedDueDiligence).HasColumnName("requires_enhanced_due_diligence");
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.WorkflowDecisionReference).HasColumnName("workflow_decision_reference").HasMaxLength(128);
            entity.Property(record => record.ApprovalRequestId).HasColumnName("approval_request_id").HasMaxLength(128);
            entity.Property(record => record.ApprovalContentHash).HasColumnName("approval_content_hash").HasMaxLength(64);
            entity.Property(record => record.SubmittedForApprovalAt).HasColumnName("submitted_for_approval_at");
            entity.Property(record => record.ApprovedAt).HasColumnName("approved_at");
            entity.Property(record => record.ProposedAt).HasColumnName("proposed_at");
            entity.Property(record => record.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(record => new { record.InstitutionId, record.CustomerId })
                .HasDatabaseName("ux_customer_classifications_approved")
                .HasFilter("status = 'approved'")
                .IsUnique();

            entity.HasOne(record => record.Customer)
                .WithMany(customer => customer.Classifications)
                .HasForeignKey(record => new { record.InstitutionId, record.CustomerId })
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    private sealed class CustomerStatusHistoryEntityConfiguration : IEntityTypeConfiguration<CustomerStatusHistoryRecord>
    {
        public void Configure(EntityTypeBuilder<CustomerStatusHistoryRecord> entity)
        {
            entity.ToTable("customer_status_history", "customer", table =>
            {
                table.HasCheckConstraint("ck_customer_status_history_sequence", "sequence >= 1");
                table.HasCheckConstraint(
                    "ck_customer_status_history_from",
                    "from_status IN ('pending', 'active', 'restricted', 'dormant')");
                table.HasCheckConstraint(
                    "ck_customer_status_history_to",
                    "to_status IN ('active', 'restricted', 'dormant', 'deceased', 'closed', 'rejected')");
            });

            entity.HasKey(record => new { record.InstitutionId, record.CustomerId, record.Sequence });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.CustomerId).HasColumnName("customer_id").HasMaxLength(64);
            entity.Property(record => record.Sequence).HasColumnName("sequence");
            entity.Property(record => record.FromStatus).HasColumnName("from_status").HasMaxLength(32);
            entity.Property(record => record.ToStatus).HasColumnName("to_status").HasMaxLength(32);
            entity.Property(record => record.Reason).HasColumnName("reason").HasMaxLength(500);
            entity.Property(record => record.ActorId).HasColumnName("actor_id").HasMaxLength(128);
            entity.Property(record => record.ApprovalReference).HasColumnName("approval_reference").HasMaxLength(128);
            entity.Property(record => record.OccurredAt).HasColumnName("occurred_at");

            entity.HasOne(record => record.Customer)
                .WithMany(customer => customer.StatusHistory)
                .HasForeignKey(record => new { record.InstitutionId, record.CustomerId })
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    private sealed class CustomerNumberSequenceEntityConfiguration : IEntityTypeConfiguration<CustomerNumberSequenceRecord>
    {
        public void Configure(EntityTypeBuilder<CustomerNumberSequenceRecord> entity)
        {
            entity.ToTable("customer_number_sequences", "customer", table =>
                table.HasCheckConstraint("ck_customer_number_sequences_next_value", "next_value >= 1"));

            entity.HasKey(record => record.InstitutionId);
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.NextValue).HasColumnName("next_value");
        }
    }
}
