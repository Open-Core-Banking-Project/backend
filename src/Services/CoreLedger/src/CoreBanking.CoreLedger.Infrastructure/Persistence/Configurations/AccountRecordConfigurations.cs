using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class AccountApplicationRecordConfiguration : IEntityTypeConfiguration<AccountApplicationRecord>
{
    public void Configure(EntityTypeBuilder<AccountApplicationRecord> builder)
    {
        builder.ToTable("account_applications", "account", table =>
        {
            table.HasCheckConstraint(
                "ck_account_applications_status",
                "status IN ('Draft', 'EligibilityPending', 'Eligible', 'Ineligible', 'PendingApproval', " +
                "'Approved', 'Rejected', 'CancelledBeforeActivation', 'Open', 'Active')");
            table.HasCheckConstraint("ck_account_applications_version", "version > 0");
        });
        builder.HasKey(x => new { x.InstitutionId, x.AccountApplicationId });
        builder.HasIndex(x => new { x.InstitutionId, x.CustomerId, x.Status });
        builder.HasIndex(x => new { x.InstitutionId, x.ProductId, x.Status });
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.AccountApplicationId).HasColumnName("account_application_id").HasMaxLength(128);
        builder.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(x => x.CustomerId).HasColumnName("customer_id").HasMaxLength(128);
        builder.Property(x => x.ProductId).HasColumnName("product_id").HasMaxLength(128);
        builder.Property(x => x.ProductCode).HasColumnName("product_code").HasMaxLength(64);
        builder.Property(x => x.ProductFamily).HasColumnName("product_family").HasMaxLength(32);
        builder.Property(x => x.ProductVersionId).HasColumnName("product_version_id").HasMaxLength(128);
        builder.Property(x => x.ProductVersionNumber).HasColumnName("product_version_number");
        builder.Property(x => x.ProductContentHash).HasColumnName("product_content_hash").HasMaxLength(64);
        builder.Property(x => x.ProductEffectiveFrom).HasColumnName("product_effective_from");
        builder.Property(x => x.ProductEffectiveTo).HasColumnName("product_effective_to");
        builder.Property(x => x.BranchId).HasColumnName("branch_id").HasMaxLength(128);
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
        builder.Property(x => x.AccountPurposeCode).HasColumnName("account_purpose_code").HasMaxLength(64);
        builder.Property(x => x.OwnershipTypeCode).HasColumnName("ownership_type_code").HasMaxLength(64);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.CreatedByActorId).HasColumnName("created_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}

internal sealed class AccountOpeningClaimRecordConfiguration : IEntityTypeConfiguration<AccountOpeningClaimRecord>
{
    public void Configure(EntityTypeBuilder<AccountOpeningClaimRecord> builder)
    {
        builder.ToTable("account_opening_claims", "account");
        builder.HasKey(x => new { x.InstitutionId, x.ClaimId });
        // One unreleased claim per customer and counting basis; this is the concurrency guard.
        builder.HasIndex(x => new { x.InstitutionId, x.CustomerId, x.LimitKey, x.LimitKeyValue })
            .IsUnique()
            .HasFilter("released_at IS NULL");
        builder.HasOne<AccountApplicationRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.AccountApplicationId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.ClaimId).HasColumnName("claim_id").HasMaxLength(128);
        builder.Property(x => x.CustomerId).HasColumnName("customer_id").HasMaxLength(128);
        builder.Property(x => x.LimitKey).HasColumnName("limit_key").HasMaxLength(32);
        builder.Property(x => x.LimitKeyValue).HasColumnName("limit_key_value").HasMaxLength(128);
        builder.Property(x => x.AccountApplicationId).HasColumnName("account_application_id").HasMaxLength(128);
        builder.Property(x => x.AcquiredAt).HasColumnName("acquired_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.ReleasedAt).HasColumnName("released_at");
    }
}

internal sealed class AccountEligibilityEvaluationRecordConfiguration
    : IEntityTypeConfiguration<AccountEligibilityEvaluationRecord>
{
    public void Configure(EntityTypeBuilder<AccountEligibilityEvaluationRecord> builder)
    {
        builder.ToTable("account_eligibility_evaluations", "account");
        builder.HasKey(x => new { x.InstitutionId, x.EvaluationId });
        builder.HasIndex(x => new { x.InstitutionId, x.AccountApplicationId, x.EvaluatedAt });
        builder.HasOne<AccountApplicationRecord>().WithMany()
            .HasForeignKey(x => new { x.InstitutionId, x.AccountApplicationId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.EvaluationId).HasColumnName("evaluation_id").HasMaxLength(128);
        builder.Property(x => x.AccountApplicationId).HasColumnName("account_application_id").HasMaxLength(128);
        builder.Property(x => x.ProductVersionId).HasColumnName("product_version_id").HasMaxLength(128);
        builder.Property(x => x.ProductContentHash).HasColumnName("product_content_hash").HasMaxLength(64);
        builder.Property(x => x.CustomerFactsVersion).HasColumnName("customer_facts_version").HasMaxLength(128);
        builder.Property(x => x.BranchReferenceVersion).HasColumnName("branch_reference_version").HasMaxLength(128);
        builder.Property(x => x.CurrencyReferenceVersion).HasColumnName("currency_reference_version").HasMaxLength(128);
        builder.Property(x => x.FactsValidUntil).HasColumnName("facts_valid_until");
        builder.Property(x => x.Eligible).HasColumnName("eligible");
        builder.Property(x => x.FactsCurrent).HasColumnName("facts_current");
        builder.Property(x => x.DecisionCode).HasColumnName("decision_code").HasMaxLength(64);
        builder.Property(x => x.TraceJson).HasColumnName("trace_json").HasColumnType("jsonb");
        builder.Property(x => x.LimitKey).HasColumnName("limit_key").HasMaxLength(32);
        builder.Property(x => x.ProductMaximum).HasColumnName("product_maximum");
        builder.Property(x => x.CountedHoldings).HasColumnName("counted_holdings");
        builder.Property(x => x.EffectiveMaximum).HasColumnName("effective_maximum");
        builder.Property(x => x.CountedHoldingIdsJson).HasColumnName("counted_holding_ids_json").HasColumnType("jsonb");
        builder.Property(x => x.PolicyVersionId).HasColumnName("policy_version_id").HasMaxLength(128);
        builder.Property(x => x.EvaluatedByActorId).HasColumnName("evaluated_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.EvaluatedAt).HasColumnName("evaluated_at");
    }
}
