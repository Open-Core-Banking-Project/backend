using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Configurations;

/// <summary>
/// Relationship-manager assignments. The filtered unique index guarantees one current assignment per customer
/// while every closed assignment stays in history. Requirement: CUS-010, CUS-026.
/// </summary>
public static class RelationshipManagerRecordConfiguration
{
    public static void ApplyRelationshipManagerRecords(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new RelationshipManagerAssignmentEntityConfiguration());
    }

    private sealed class RelationshipManagerAssignmentEntityConfiguration
        : IEntityTypeConfiguration<RelationshipManagerAssignmentRecord>
    {
        public void Configure(EntityTypeBuilder<RelationshipManagerAssignmentRecord> entity)
        {
            entity.ToTable("relationship_manager_assignments", "customer", table =>
            {
                table.HasCheckConstraint(
                    "ck_relationship_manager_assignments_period",
                    "effective_to IS NULL OR effective_to >= effective_from");
                table.HasCheckConstraint(
                    "ck_relationship_manager_assignments_manager",
                    "length(btrim(manager_actor_id)) > 0");
            });

            entity.HasKey(record => new { record.InstitutionId, record.AssignmentId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.AssignmentId).HasColumnName("assignment_id").HasMaxLength(64);
            entity.Property(record => record.CustomerId).HasColumnName("customer_id").HasMaxLength(64);
            entity.Property(record => record.ManagerActorId).HasColumnName("manager_actor_id").HasMaxLength(128);
            entity.Property(record => record.ScopeBranchId).HasColumnName("scope_branch_id").HasMaxLength(64);
            entity.Property(record => record.PreviousAssignmentId).HasColumnName("previous_assignment_id").HasMaxLength(64);
            entity.Property(record => record.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(record => record.EffectiveTo).HasColumnName("effective_to");
            entity.Property(record => record.AssignedBy).HasColumnName("assigned_by").HasMaxLength(128);
            entity.Property(record => record.Reason).HasColumnName("reason").HasMaxLength(500);

            entity.HasOne(record => record.Customer)
                .WithMany()
                .HasForeignKey(record => new { record.InstitutionId, record.CustomerId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(record => new { record.InstitutionId, record.CustomerId })
                .HasDatabaseName("ux_relationship_manager_assignments_current")
                .HasFilter("effective_to IS NULL")
                .IsUnique();
            entity.HasIndex(record => new { record.InstitutionId, record.ManagerActorId })
                .HasDatabaseName("ix_relationship_manager_assignments_manager");
        }
    }
}
