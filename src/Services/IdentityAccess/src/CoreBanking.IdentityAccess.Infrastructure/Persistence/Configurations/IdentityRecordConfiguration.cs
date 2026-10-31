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
        builder.ToTable("identities", "identity", table =>
        {
            table.HasCheckConstraint(
                "ck_identities_identity_type",
                "identity_type IN ('Customer', 'Staff', 'Agent', 'CorporateUser', 'Administrator', 'Service')");
            table.HasCheckConstraint(
                "ck_identities_status",
                "status IN ('Pending', 'Active', 'Locked', 'Suspended', 'Disabled', 'Closed')");
            table.HasCheckConstraint("ck_identities_concurrency_version", "concurrency_version > 0");
        });
        builder.HasKey(identity => new { identity.InstitutionId, identity.IdentityId });

        builder.Property(identity => identity.InstitutionId).HasColumnName("institution_id");
        builder.Property(identity => identity.IdentityId).HasColumnName("identity_id");
        builder.Property(identity => identity.ActorType).HasColumnName("actor_type").HasConversion<string>();
        builder.Property(identity => identity.IdentityType).HasColumnName("identity_type").HasConversion<string>();
        builder.Property(identity => identity.DisplayName).HasColumnName("display_name");
        builder.Property(identity => identity.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(identity => identity.CreatedAt).HasColumnName("created_at");
        builder.Property(identity => identity.UpdatedAt).HasColumnName("updated_at");
        builder.Property(identity => identity.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();

    }
}

internal sealed class InstitutionMembershipRecordConfiguration : IEntityTypeConfiguration<InstitutionMembershipRecord>
{
    public void Configure(EntityTypeBuilder<InstitutionMembershipRecord> builder)
    {
        builder.ToTable("institution_memberships", "identity", table =>
        {
            table.HasCheckConstraint(
                "ck_institution_memberships_status",
                "status IN ('Pending', 'Active', 'Locked', 'Suspended', 'Disabled', 'Closed')");
            table.HasCheckConstraint(
                "ck_institution_memberships_concurrency_version",
                "concurrency_version > 0");
        });
        builder.HasKey(membership => new { membership.InstitutionId, membership.MembershipId });

        builder.Property(membership => membership.InstitutionId).HasColumnName("institution_id");
        builder.Property(membership => membership.MembershipId).HasColumnName("membership_id");
        builder.Property(membership => membership.IdentityId).HasColumnName("identity_id");
        builder.Property(membership => membership.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(membership => membership.CreatedAt).HasColumnName("created_at");
        builder.Property(membership => membership.UpdatedAt).HasColumnName("updated_at");
        builder.Property(membership => membership.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();

        builder.HasIndex(membership => new { membership.InstitutionId, membership.IdentityId }).IsUnique();
        builder.HasOne<IdentityRecord>()
            .WithMany()
            .HasForeignKey(membership => new { membership.InstitutionId, membership.IdentityId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProviderIdentityLinkRecordConfiguration : IEntityTypeConfiguration<ProviderIdentityLinkRecord>
{
    public void Configure(EntityTypeBuilder<ProviderIdentityLinkRecord> builder)
    {
        builder.ToTable("identity_links", "identity", table => table.HasCheckConstraint(
            "ck_identity_links_status",
            "status IN ('Active', 'Suspended', 'Archived')"));
        builder.HasKey(link => new { link.InstitutionId, link.IdentityLinkId });

        builder.Property(link => link.InstitutionId).HasColumnName("institution_id");
        builder.Property(link => link.IdentityLinkId).HasColumnName("identity_link_id");
        builder.Property(link => link.IdentityId).HasColumnName("identity_id");
        builder.Property(link => link.Provider).HasColumnName("provider").HasMaxLength(100);
        builder.Property(link => link.Realm).HasColumnName("realm").HasMaxLength(200);
        builder.Property(link => link.SubjectFingerprint).HasColumnName("subject_fingerprint").HasMaxLength(64);
        builder.Property(link => link.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(link => link.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(link => new { link.InstitutionId, link.Provider, link.Realm, link.SubjectFingerprint }).IsUnique();
        builder.HasIndex(link => new { link.InstitutionId, link.IdentityId });
        builder.HasOne<IdentityRecord>()
            .WithMany()
            .HasForeignKey(link => new { link.InstitutionId, link.IdentityId })
            .OnDelete(DeleteBehavior.Restrict);
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
        builder.Property(client => client.CredentialReference).HasColumnName("credential_reference").HasMaxLength(200);
        builder.Property(client => client.CredentialExpiresAt).HasColumnName("credential_expires_at");
        builder.Property(client => client.PreviousCredentialReference).HasColumnName("previous_credential_reference").HasMaxLength(200);
        builder.Property(client => client.PreviousValidUntil).HasColumnName("previous_valid_until");
        builder.Property(client => client.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(client => client.CreatedAt).HasColumnName("created_at");
        builder.Property(client => client.UpdatedAt).HasColumnName("updated_at");
    }
}

internal sealed class SessionRecordConfiguration : IEntityTypeConfiguration<SessionRecord>
{
    public void Configure(EntityTypeBuilder<SessionRecord> builder)
    {
        builder.ToTable("sessions", "identity", table =>
        {
            table.HasCheckConstraint(
                "ck_sessions_status",
                "status IN ('Active', 'Refreshed', 'Revoked', 'Compromised', 'Expired')");
            table.HasCheckConstraint("ck_sessions_concurrency_version", "concurrency_version >= 0");
        });
        builder.HasKey(session => new { session.InstitutionId, session.SessionId });

        builder.Property(session => session.InstitutionId).HasColumnName("institution_id");
        builder.Property(session => session.SessionId).HasColumnName("session_id");
        builder.Property(session => session.IdentityId).HasColumnName("identity_id");
        builder.Property(session => session.DeviceId).HasColumnName("device_id");
        builder.Property(session => session.ProviderSessionFingerprint).HasColumnName("provider_session_fingerprint").HasMaxLength(64);
        builder.Property(session => session.AuthenticationStrength).HasColumnName("authentication_strength").HasConversion<string>();
        builder.Property(session => session.IssuedAt).HasColumnName("issued_at");
        builder.Property(session => session.ExpiresAt).HasColumnName("expires_at");
        builder.Property(session => session.LastActivityAt).HasColumnName("last_activity_at");
        builder.Property(session => session.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(session => session.RevokedAt).HasColumnName("revoked_at");
        builder.Property(session => session.ProviderRevocationRequestedAt).HasColumnName("provider_revocation_requested_at");
        builder.Property(session => session.RevocationEventId).HasColumnName("revocation_event_id");
        builder.Property(session => session.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();

        builder.HasIndex(session => new { session.InstitutionId, session.IdentityId });
        builder.HasIndex(session => new { session.InstitutionId, session.ProviderSessionFingerprint }).IsUnique();
        builder.HasOne<IdentityRecord>()
            .WithMany()
            .HasForeignKey(session => new { session.InstitutionId, session.IdentityId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
