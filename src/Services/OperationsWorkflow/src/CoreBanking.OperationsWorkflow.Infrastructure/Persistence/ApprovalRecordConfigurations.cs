using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.OperationsWorkflow.Infrastructure.Persistence;

internal sealed class ApprovalRequestRecordConfiguration : IEntityTypeConfiguration<ApprovalRequestRecord>
{
    public void Configure(EntityTypeBuilder<ApprovalRequestRecord> builder)
    {
        builder.ToTable("approval_requests", "workflow", table =>
            table.HasCheckConstraint("ck_approval_requests_status", "status IN ('submitted', 'approved', 'rejected')"));
        builder.HasKey(x => new { x.InstitutionId, x.RequestId });
        builder.HasAlternateKey(x => new { x.InstitutionId, x.RequestId, x.SubjectVersion, x.SubjectHash });
        builder.HasIndex(x => new { x.InstitutionId, x.SourceService, x.SourceRequestKey }).IsUnique();
        builder.HasIndex(x => new { x.InstitutionId, x.SubjectType, x.SubjectId, x.SubjectVersion, x.SubjectHash });
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(x => x.SourceRequestKey).HasColumnName("source_request_key").HasMaxLength(128);
        builder.Property(x => x.WorkflowKey).HasColumnName("workflow_key").HasMaxLength(128);
        builder.Property(x => x.WorkflowVersionId).HasColumnName("workflow_version_id").HasMaxLength(128);
        builder.Property(x => x.WorkflowDefinitionHash).HasColumnName("workflow_definition_hash").HasMaxLength(64);
        builder.Property(x => x.SourceService).HasColumnName("source_service").HasMaxLength(64);
        builder.Property(x => x.SubjectType).HasColumnName("subject_type").HasMaxLength(128);
        builder.Property(x => x.SubjectId).HasColumnName("subject_id").HasMaxLength(128);
        builder.Property(x => x.SubjectVersion).HasColumnName("subject_version").HasMaxLength(128);
        builder.Property(x => x.SubjectHash).HasColumnName("subject_hash").HasMaxLength(64);
        builder.Property(x => x.MakerActorId).HasColumnName("maker_actor_id").HasMaxLength(128);
        builder.Property(x => x.RequiredPermission).HasColumnName("required_permission").HasMaxLength(128);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at");
        builder.Property(x => x.ApplicationStatus).HasColumnName("application_status").HasMaxLength(32);
        builder.Property(x => x.AppliedAt).HasColumnName("applied_at");
        builder.Property(x => x.ApplyFailureCode).HasColumnName("apply_failure_code").HasMaxLength(64);
    }
}

/// <summary>
/// The source-service acknowledgement table. The unique result identifier is the idempotency key and the row is
/// append-only evidence of what the owning service reported. Decision: epic 06 section 11.
/// </summary>
internal sealed class ApprovalApplicationResultRecordConfiguration : IEntityTypeConfiguration<ApprovalApplicationResultRecord>
{
    public void Configure(EntityTypeBuilder<ApprovalApplicationResultRecord> builder)
    {
        builder.ToTable("application_results", "approval", table =>
        {
            table.HasCheckConstraint("ck_application_results_outcome", "outcome IN ('applied', 'rejected', 'apply_failed')");
            table.HasCheckConstraint(
                "ck_application_results_failure_code",
                "outcome <> 'apply_failed' OR failure_code IS NOT NULL");
        });
        builder.HasKey(x => new { x.InstitutionId, x.ResultId });
        builder.HasIndex(x => new { x.InstitutionId, x.RequestId }).IsUnique();
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.ResultId).HasColumnName("result_id").HasMaxLength(128);
        builder.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(x => x.DecisionId).HasColumnName("decision_id").HasMaxLength(128);
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32);
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(64);
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(x => x.SourceService).HasColumnName("source_service").HasMaxLength(64);
        builder.Property(x => x.SubjectVersion).HasColumnName("subject_version").HasMaxLength(128);
        builder.Property(x => x.SubjectHash).HasColumnName("subject_hash").HasMaxLength(64);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.AcknowledgedAt).HasColumnName("acknowledged_at");
    }
}

internal sealed class ApprovalDecisionRecordConfiguration : IEntityTypeConfiguration<ApprovalDecisionRecord>
{
    public void Configure(EntityTypeBuilder<ApprovalDecisionRecord> builder)
    {
        builder.ToTable("approval_decisions", "workflow", table =>
        {
            table.HasCheckConstraint("ck_approval_decisions_decision", "decision IN ('approved', 'rejected')");
            table.HasCheckConstraint("ck_approval_decisions_entitlement_period", "entitlement_valid_until > entitlement_valid_from");
            table.HasCheckConstraint(
                "ck_approval_decisions_entitlement_current",
                "decided_at >= entitlement_valid_from AND decided_at < entitlement_valid_until");
            table.HasCheckConstraint("ck_approval_decisions_rejection_reason", "decision <> 'rejected' OR reason IS NOT NULL");
        });
        builder.HasKey(x => new { x.InstitutionId, x.DecisionId });
        builder.HasIndex(x => new { x.InstitutionId, x.RequestId }).IsUnique();
        builder.HasOne<ApprovalRequestRecord>().WithOne()
            .HasForeignKey<ApprovalDecisionRecord>(x => new { x.InstitutionId, x.RequestId, x.SubjectVersion, x.SubjectHash })
            .HasPrincipalKey<ApprovalRequestRecord>(x => new { x.InstitutionId, x.RequestId, x.SubjectVersion, x.SubjectHash })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.DecisionId).HasColumnName("decision_id").HasMaxLength(128);
        builder.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(32);
        builder.Property(x => x.SubjectVersion).HasColumnName("subject_version").HasMaxLength(128);
        builder.Property(x => x.SubjectHash).HasColumnName("subject_hash").HasMaxLength(64);
        builder.Property(x => x.ApproverActorId).HasColumnName("approver_actor_id").HasMaxLength(128);
        builder.Property(x => x.Permission).HasColumnName("permission").HasMaxLength(128);
        builder.Property(x => x.EntitlementVersion).HasColumnName("entitlement_version").HasMaxLength(128);
        builder.Property(x => x.EntitlementValidFrom).HasColumnName("entitlement_valid_from");
        builder.Property(x => x.EntitlementValidUntil).HasColumnName("entitlement_valid_until");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1024);
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at");
    }
}
