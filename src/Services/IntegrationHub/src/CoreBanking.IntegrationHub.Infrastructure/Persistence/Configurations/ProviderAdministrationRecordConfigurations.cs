using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.IntegrationHub.Infrastructure.Persistence.Configurations;

public sealed class CredentialReferenceRecordConfiguration : IEntityTypeConfiguration<CredentialReferenceRecord>
{
    public void Configure(EntityTypeBuilder<CredentialReferenceRecord> builder)
    {
        builder.ToTable("credential_references", "provider", table =>
        {
            table.HasCheckConstraint("ck_credential_references_version", "version > 0");
            table.HasCheckConstraint("ck_credential_references_status", "status IN ('active', 'superseded', 'revoked')");
        });
        builder.HasKey(record => record.RecordId);
        builder.HasIndex(record => new { record.InstitutionId, record.CredentialReferenceId, record.Version }).IsUnique();
        builder.HasIndex(record => new { record.InstitutionId, record.Environment, record.ProviderCode, record.ReferenceName, record.Version }).IsUnique();
        builder.Property(record => record.RecordId).HasColumnName("record_id").HasMaxLength(128);
        builder.Property(record => record.CredentialReferenceId).HasColumnName("credential_reference_id").HasMaxLength(128);
        builder.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(record => record.Environment).HasColumnName("environment").HasMaxLength(32);
        builder.Property(record => record.ProviderCode).HasColumnName("provider_code").HasMaxLength(64);
        builder.Property(record => record.Kind).HasColumnName("kind").HasMaxLength(32);
        builder.Property(record => record.ReferenceName).HasColumnName("reference_name").HasMaxLength(256);
        builder.Property(record => record.Owner).HasColumnName("owner").HasMaxLength(128);
        builder.Property(record => record.Version).HasColumnName("version");
        builder.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(record => record.ExpiresAt).HasColumnName("expires_at");
        builder.Property(record => record.PreviousRecordId).HasColumnName("previous_record_id").HasMaxLength(128);
        builder.Property(record => record.ActorId).HasColumnName("actor_id").HasMaxLength(128);
        builder.Property(record => record.Reason).HasColumnName("reason").HasMaxLength(512);
        builder.Property(record => record.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(record => record.CreatedAt).HasColumnName("created_at");
    }
}

public sealed class ApiClientRecordConfiguration : IEntityTypeConfiguration<ApiClientRecord>
{
    public void Configure(EntityTypeBuilder<ApiClientRecord> builder)
    {
        builder.ToTable("api_clients", "partner", table =>
            table.HasCheckConstraint("ck_api_clients_status", "status IN ('active', 'revoked')"));
        builder.HasKey(record => record.ApiClientId);
        builder.HasIndex(record => new { record.InstitutionId, record.Environment, record.ClientIdentifier }).IsUnique();
        builder.Property(record => record.ApiClientId).HasColumnName("api_client_id").HasMaxLength(128);
        builder.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(record => record.Environment).HasColumnName("environment").HasMaxLength(32);
        builder.Property(record => record.ClientIdentifier).HasColumnName("client_identifier").HasMaxLength(128);
        builder.Property(record => record.DisplayName).HasColumnName("display_name").HasMaxLength(128);
        builder.Property(record => record.AllowedScopesJson).HasColumnName("allowed_scopes_json").HasColumnType("jsonb");
        builder.Property(record => record.AllowedNetworkRangesJson).HasColumnName("allowed_network_ranges_json").HasColumnType("jsonb");
        builder.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(record => record.ActorId).HasColumnName("actor_id").HasMaxLength(128);
        builder.Property(record => record.Reason).HasColumnName("reason").HasMaxLength(512);
        builder.Property(record => record.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(record => record.CreatedAt).HasColumnName("created_at");
        builder.Property(record => record.RevokedAt).HasColumnName("revoked_at");
        builder.Property(record => record.RevokedBy).HasColumnName("revoked_by").HasMaxLength(128);
        builder.Property(record => record.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(512);
        builder.Property(record => record.Version).HasColumnName("xmin").IsRowVersion();
    }
}
