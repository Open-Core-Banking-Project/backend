using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>party</c> schema. Personal data is stored only in protected form, every table is
/// tenant-owned, and versioned rows carry an effective period instead of being overwritten.
/// Decision: epic 03 party model, CUS-028.
/// </summary>
public static class PartyRecordConfiguration
{
    private const string IndividualPartyType = "individual";

    public static void ApplyPartyRecords(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new PartyEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PartyIndividualEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PartyNameEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PartyContactEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PartyAddressEntityConfiguration());
    }

    private sealed class PartyEntityConfiguration : IEntityTypeConfiguration<PartyRecord>
    {
        public void Configure(EntityTypeBuilder<PartyRecord> entity)
        {
            entity.ToTable("parties", "party", table =>
            {
                table.HasCheckConstraint("ck_parties_type", $"party_type = '{IndividualPartyType}'");
                table.HasCheckConstraint("ck_parties_status", "status IN ('pending', 'active', 'restricted', 'merged', 'closed')");
                table.HasCheckConstraint("ck_parties_concurrency_version", "concurrency_version > 0");
            });

            entity.HasKey(record => new { record.InstitutionId, record.PartyId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.PartyId).HasColumnName("party_id").HasMaxLength(64);
            entity.Property(record => record.PartyType).HasColumnName("party_type").HasMaxLength(32);
            entity.Property(record => record.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(record => record.CreatedAt).HasColumnName("created_at");
            entity.Property(record => record.UpdatedAt).HasColumnName("updated_at");
            entity.Property(record => record.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();

            entity.HasIndex(record => new { record.InstitutionId, record.Status, record.UpdatedAt })
                .HasDatabaseName("ix_parties_institution_status_updated");
        }
    }

    private sealed class PartyIndividualEntityConfiguration : IEntityTypeConfiguration<PartyIndividualRecord>
    {
        public void Configure(EntityTypeBuilder<PartyIndividualRecord> entity)
        {
            entity.ToTable("individuals", "party", table =>
            {
                table.HasCheckConstraint("ck_party_individuals_birth_year", "birth_year BETWEEN 1850 AND 2200");
                table.HasCheckConstraint("ck_party_individuals_birth_lookup_token", "char_length(birth_date_lookup_token) IN (0, 64)");
            });

            entity.HasKey(record => new { record.InstitutionId, record.PartyId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.PartyId).HasColumnName("party_id").HasMaxLength(64);
            entity.Property(record => record.BirthDateToken).HasColumnName("birth_date_token").HasMaxLength(512);
            entity.Property(record => record.BirthDateLookupToken).HasColumnName("birth_date_lookup_token").HasMaxLength(64);
            entity.Property(record => record.BirthDateMasked).HasColumnName("birth_date_masked").HasMaxLength(32);
            entity.Property(record => record.BirthYear).HasColumnName("birth_year");
            entity.Property(record => record.CountryOfBirth).HasColumnName("country_of_birth").HasMaxLength(2);
            entity.Property(record => record.CapturedAt).HasColumnName("captured_at");

            entity.HasIndex(record => new { record.InstitutionId, record.BirthDateLookupToken })
                .HasDatabaseName("ix_party_individuals_institution_birth_lookup");

            entity.HasOne(record => record.Party)
                .WithOne(record => record.Individual)
                .HasForeignKey<PartyIndividualRecord>(record => new { record.InstitutionId, record.PartyId })
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    private sealed class PartyNameEntityConfiguration : IEntityTypeConfiguration<PartyNameRecord>
    {
        public void Configure(EntityTypeBuilder<PartyNameRecord> entity)
        {
            entity.ToTable("names", "party", table =>
            {
                table.HasCheckConstraint("ck_party_names_type", "name_type IN ('legal', 'preferred', 'former')");
                table.HasCheckConstraint("ck_party_names_effective_period", "effective_to IS NULL OR effective_to > effective_from");
            });

            entity.HasKey(record => new { record.InstitutionId, record.NameId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.NameId).HasColumnName("name_id").HasMaxLength(64);
            entity.Property(record => record.PartyId).HasColumnName("party_id").HasMaxLength(64);
            entity.Property(record => record.NameType).HasColumnName("name_type").HasMaxLength(32);
            entity.Property(record => record.GivenName).HasColumnName("given_name").HasMaxLength(200);
            entity.Property(record => record.MiddleName).HasColumnName("middle_name").HasMaxLength(200);
            entity.Property(record => record.FamilyName).HasColumnName("family_name").HasMaxLength(200);
            entity.Property(record => record.Normalised).HasColumnName("normalised").HasMaxLength(600);
            entity.Property(record => record.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(record => record.EffectiveTo).HasColumnName("effective_to");

            entity.HasOne(record => record.Party)
                .WithMany(record => record.Names)
                .HasForeignKey(record => new { record.InstitutionId, record.PartyId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(record => new { record.InstitutionId, record.PartyId, record.EffectiveTo })
                .HasDatabaseName("ix_party_names_institution_party_effective");
            entity.HasIndex(record => new { record.InstitutionId, record.Normalised })
                .HasDatabaseName("ix_party_names_institution_normalised");
        }
    }

    private sealed class PartyContactEntityConfiguration : IEntityTypeConfiguration<PartyContactRecord>
    {
        public void Configure(EntityTypeBuilder<PartyContactRecord> entity)
        {
            entity.ToTable("contacts", "party", table =>
            {
                table.HasCheckConstraint("ck_party_contacts_type", "contact_type IN ('mobile', 'email', 'landline')");
                table.HasCheckConstraint("ck_party_contacts_lookup_token", "char_length(lookup_token) = 64");
                table.HasCheckConstraint("ck_party_contacts_effective_period", "effective_to IS NULL OR effective_to > effective_from");
            });

            entity.HasKey(record => new { record.InstitutionId, record.ContactId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.ContactId).HasColumnName("contact_id").HasMaxLength(64);
            entity.Property(record => record.PartyId).HasColumnName("party_id").HasMaxLength(64);
            entity.Property(record => record.ContactType).HasColumnName("contact_type").HasMaxLength(32);
            entity.Property(record => record.ValueToken).HasColumnName("value_token").HasMaxLength(512);
            entity.Property(record => record.MaskedValue).HasColumnName("masked_value").HasMaxLength(64);
            entity.Property(record => record.LookupToken).HasColumnName("lookup_token").HasMaxLength(64);
            entity.Property(record => record.CapturedAt).HasColumnName("captured_at");
            entity.Property(record => record.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(record => record.EffectiveTo).HasColumnName("effective_to");

            entity.HasOne(record => record.Party)
                .WithMany(record => record.Contacts)
                .HasForeignKey(record => new { record.InstitutionId, record.PartyId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(record => new { record.InstitutionId, record.PartyId, record.LookupToken })
                .HasDatabaseName("ux_party_contacts_active_lookup")
                .HasFilter("effective_to IS NULL")
                .IsUnique();
            entity.HasIndex(record => new { record.InstitutionId, record.LookupToken })
                .HasDatabaseName("ix_party_contacts_institution_lookup");
        }
    }

    private sealed class PartyAddressEntityConfiguration : IEntityTypeConfiguration<PartyAddressRecord>
    {
        public void Configure(EntityTypeBuilder<PartyAddressRecord> entity)
        {
            entity.ToTable("addresses", "party", table =>
            {
                table.HasCheckConstraint("ck_party_addresses_type", "address_type IN ('residential', 'postal', 'business')");
                table.HasCheckConstraint("ck_party_addresses_country", "country_code ~ '^[A-Z]{2}$'");
                table.HasCheckConstraint("ck_party_addresses_effective_period", "effective_to IS NULL OR effective_to > effective_from");
                table.HasCheckConstraint("ck_party_addresses_lookup_token", "char_length(lookup_token) IN (0, 64)");
            });

            entity.HasKey(record => new { record.InstitutionId, record.AddressId });
            entity.Property(record => record.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
            entity.Property(record => record.AddressId).HasColumnName("address_id").HasMaxLength(64);
            entity.Property(record => record.PartyId).HasColumnName("party_id").HasMaxLength(64);
            entity.Property(record => record.AddressType).HasColumnName("address_type").HasMaxLength(32);
            entity.Property(record => record.LineToken).HasColumnName("line_token").HasMaxLength(512);
            entity.Property(record => record.LookupToken).HasColumnName("lookup_token").HasMaxLength(64);
            entity.Property(record => record.MaskedLine).HasColumnName("masked_line").HasMaxLength(64);
            entity.Property(record => record.City).HasColumnName("city").HasMaxLength(128);
            entity.Property(record => record.Region).HasColumnName("region").HasMaxLength(128);
            entity.Property(record => record.CountryCode).HasColumnName("country_code").HasMaxLength(2);
            entity.Property(record => record.CapturedAt).HasColumnName("captured_at");
            entity.Property(record => record.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(record => record.EffectiveTo).HasColumnName("effective_to");

            entity.HasOne(record => record.Party)
                .WithMany(record => record.Addresses)
                .HasForeignKey(record => new { record.InstitutionId, record.PartyId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(record => new { record.InstitutionId, record.PartyId, record.AddressType })
                .HasDatabaseName("ix_party_addresses_institution_party_type");
            entity.HasIndex(record => new { record.InstitutionId, record.LookupToken })
                .HasDatabaseName("ix_party_addresses_institution_lookup");
        }
    }
}
