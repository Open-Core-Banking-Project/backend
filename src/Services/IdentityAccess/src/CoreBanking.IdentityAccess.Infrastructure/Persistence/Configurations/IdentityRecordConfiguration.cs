using CoreBanking.IdentityAccess.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mappings for the <c>identity</c> schema. Every table implements <see cref="ITenantOwnedEntity"/>;
/// the institution column leads each key and each foreign key so a reference can never cross tenants.
/// Decision: ADR-0004, ADR-0008.
/// </summary>
internal sealed class IdentityRecordConfiguration : IEntityTypeConfiguration<IdentityRecord>
{
    public void Configure(EntityTypeBuilder<IdentityRecord> builder)
    {
        builder.ToTable("identities", "identity");
        builder.HasKey(identity => new { identity.InstitutionId, identity.IdentityId });

        builder.Property(identity => identity.InstitutionId).HasColumnName("institution_id");
        builder.Property(identity => identity.IdentityId).HasColumnName("identity_id");
        builder.Property(identity => identity.ActorType).HasColumnName("actor_type").HasConversion<string>();
        builder.Property(identity => identity.DisplayName).HasColumnName("display_name");
        builder.Property(identity => identity.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(identity => identity.CreatedAt).HasColumnName("created_at");
        builder.Property(identity => identity.UpdatedAt).HasColumnName("updated_at");
    }
}

internal sealed class ServiceClientRecordConfiguration : IEntityTypeConfiguration<ServiceClientRecord>
{
    public void Configure(EntityTypeBuilder<ServiceClientRecord> builder)
    {
        builder.ToTable("service_clients", "identity");
        builder.HasKey(client => new { client.InstitutionId, client.ServiceClientId });

        builder.Property(client => client.InstitutionId).HasColumnName("institution_id");
        builder.Property(client => client.ServiceClientId).HasColumnName("service_client_id");
        builder.Property(client => client.ClientName).HasColumnName("client_name");
        builder.Property(client => client.Audience).HasColumnName("audience");
        builder.Property(client => client.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(client => client.CreatedAt).HasColumnName("created_at");
        builder.Property(client => client.UpdatedAt).HasColumnName("updated_at");
    }
}

internal sealed class SessionRecordConfiguration : IEntityTypeConfiguration<SessionRecord>
{
    public void Configure(EntityTypeBuilder<SessionRecord> builder)
    {
        builder.ToTable("sessions", "identity");
        builder.HasKey(session => new { session.InstitutionId, session.SessionId });

        builder.Property(session => session.InstitutionId).HasColumnName("institution_id");
        builder.Property(session => session.SessionId).HasColumnName("session_id");
        builder.Property(session => session.IdentityId).HasColumnName("identity_id");
        builder.Property(session => session.DeviceId).HasColumnName("device_id");
        builder.Property(session => session.AuthenticationStrength).HasColumnName("authentication_strength").HasConversion<string>();
        builder.Property(session => session.IssuedAt).HasColumnName("issued_at");
        builder.Property(session => session.ExpiresAt).HasColumnName("expires_at");
        builder.Property(session => session.RevokedAt).HasColumnName("revoked_at");

        builder.HasIndex(session => new { session.InstitutionId, session.IdentityId });
        builder.HasOne<IdentityRecord>()
            .WithMany()
            .HasForeignKey(session => new { session.InstitutionId, session.IdentityId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
