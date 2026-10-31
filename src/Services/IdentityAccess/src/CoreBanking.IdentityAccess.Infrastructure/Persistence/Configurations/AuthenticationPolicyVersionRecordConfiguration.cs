using CoreBanking.IdentityAccess.Infrastructure.Persistence.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Configurations;

internal sealed class AuthenticationPolicyVersionRecordConfiguration : IEntityTypeConfiguration<AuthenticationPolicyVersionRecord>
{
    public void Configure(EntityTypeBuilder<AuthenticationPolicyVersionRecord> builder)
    {
        builder.ToTable("authentication_policy_versions", "security");
        builder.HasKey(policy => new { policy.InstitutionId, policy.AuthenticationPolicyVersionId });
        builder.Property(policy => policy.InstitutionId).HasColumnName("institution_id");
        builder.Property(policy => policy.AuthenticationPolicyVersionId).HasColumnName("authentication_policy_version_id");
        builder.Property(policy => policy.Channel).HasColumnName("channel").HasConversion<string>();
        builder.Property(policy => policy.Provider).HasColumnName("provider").HasMaxLength(100);
        builder.Property(policy => policy.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(policy => policy.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(policy => policy.EffectiveUntil).HasColumnName("effective_until");
        builder.Property(policy => policy.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(policy => new { policy.InstitutionId, policy.Channel, policy.Status, policy.EffectiveFrom });
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_authentication_policy_versions_effective_interval",
            "effective_until IS NULL OR effective_until > effective_from"));
    }
}
