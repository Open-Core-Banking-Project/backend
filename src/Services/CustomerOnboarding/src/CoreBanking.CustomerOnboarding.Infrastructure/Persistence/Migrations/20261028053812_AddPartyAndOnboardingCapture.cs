using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyAndOnboardingCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "party");

            migrationBuilder.EnsureSchema(
                name: "onboarding");

            migrationBuilder.CreateTable(
                name: "applications",
                schema: "onboarding",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    application_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    application_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    application_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    branch_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    current_capture = table.Column<string>(type: "jsonb", nullable: false),
                    current_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    party_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => new { x.institution_id, x.application_id });
                    table.CheckConstraint("ck_onboarding_applications_concurrency_version", "concurrency_version > 0");
                    table.CheckConstraint("ck_onboarding_applications_payload_hash", "char_length(current_payload_hash) = 64");
                    table.CheckConstraint("ck_onboarding_applications_revision", "revision >= 1");
                    table.CheckConstraint("ck_onboarding_applications_status", "status IN ('draft', 'submitted', 'withdrawn', 'expired', 'rejected', 'approved', 'completed')");
                    table.CheckConstraint("ck_onboarding_applications_submitted_party", "status <> 'submitted' OR party_id IS NOT NULL");
                    table.CheckConstraint("ck_onboarding_applications_type", "application_type = 'individual'");
                    table.CheckConstraint("ck_onboarding_applications_withdrawn_at", "(status = 'withdrawn') = (withdrawn_at IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "parties",
                schema: "party",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    party_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    party_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parties", x => new { x.institution_id, x.party_id });
                    table.CheckConstraint("ck_parties_concurrency_version", "concurrency_version > 0");
                    table.CheckConstraint("ck_parties_status", "status IN ('pending', 'active', 'restricted', 'merged', 'closed')");
                    table.CheckConstraint("ck_parties_type", "party_type = 'individual'");
                });

            migrationBuilder.CreateTable(
                name: "application_revisions",
                schema: "onboarding",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    application_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    capture = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    recorded_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_revisions", x => new { x.institution_id, x.application_id, x.revision });
                    table.CheckConstraint("ck_onboarding_revisions_payload_hash", "char_length(payload_hash) = 64");
                    table.CheckConstraint("ck_onboarding_revisions_reason", "reason IN ('captured', 'submitted', 'withdrawn')");
                    table.CheckConstraint("ck_onboarding_revisions_revision", "revision >= 1");
                    table.ForeignKey(
                        name: "FK_application_revisions_applications_institution_id_applicati~",
                        columns: x => new { x.institution_id, x.application_id },
                        principalSchema: "onboarding",
                        principalTable: "applications",
                        principalColumns: new[] { "institution_id", "application_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "addresses",
                schema: "party",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    address_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    party_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    address_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    line_token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    masked_line = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    city = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    region = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_addresses", x => new { x.institution_id, x.address_id });
                    table.CheckConstraint("ck_party_addresses_country", "country_code ~ '^[A-Z]{2}$'");
                    table.CheckConstraint("ck_party_addresses_effective_period", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("ck_party_addresses_type", "address_type IN ('residential', 'postal', 'business')");
                    table.ForeignKey(
                        name: "FK_addresses_parties_institution_id_party_id",
                        columns: x => new { x.institution_id, x.party_id },
                        principalSchema: "party",
                        principalTable: "parties",
                        principalColumns: new[] { "institution_id", "party_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                schema: "party",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    contact_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    party_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    contact_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    value_token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    masked_value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    lookup_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contacts", x => new { x.institution_id, x.contact_id });
                    table.CheckConstraint("ck_party_contacts_effective_period", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("ck_party_contacts_lookup_token", "char_length(lookup_token) = 64");
                    table.CheckConstraint("ck_party_contacts_type", "contact_type IN ('mobile', 'email', 'landline')");
                    table.ForeignKey(
                        name: "FK_contacts_parties_institution_id_party_id",
                        columns: x => new { x.institution_id, x.party_id },
                        principalSchema: "party",
                        principalTable: "parties",
                        principalColumns: new[] { "institution_id", "party_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "individuals",
                schema: "party",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    party_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    birth_date_token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    birth_date_masked = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    birth_year = table.Column<int>(type: "integer", nullable: false),
                    country_of_birth = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_individuals", x => new { x.institution_id, x.party_id });
                    table.CheckConstraint("ck_party_individuals_birth_year", "birth_year BETWEEN 1850 AND 2200");
                    table.ForeignKey(
                        name: "FK_individuals_parties_institution_id_party_id",
                        columns: x => new { x.institution_id, x.party_id },
                        principalSchema: "party",
                        principalTable: "parties",
                        principalColumns: new[] { "institution_id", "party_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "names",
                schema: "party",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    party_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    given_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    family_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalised = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_names", x => new { x.institution_id, x.name_id });
                    table.CheckConstraint("ck_party_names_effective_period", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("ck_party_names_type", "name_type IN ('legal', 'preferred', 'former')");
                    table.ForeignKey(
                        name: "FK_names_parties_institution_id_party_id",
                        columns: x => new { x.institution_id, x.party_id },
                        principalSchema: "party",
                        principalTable: "parties",
                        principalColumns: new[] { "institution_id", "party_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_party_addresses_institution_party_type",
                schema: "party",
                table: "addresses",
                columns: new[] { "institution_id", "party_id", "address_type" });

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_applications_institution_status_updated",
                schema: "onboarding",
                table: "applications",
                columns: new[] { "institution_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_onboarding_applications_reference",
                schema: "onboarding",
                table: "applications",
                columns: new[] { "institution_id", "application_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_party_contacts_institution_lookup",
                schema: "party",
                table: "contacts",
                columns: new[] { "institution_id", "lookup_token" });

            migrationBuilder.CreateIndex(
                name: "ux_party_contacts_active_lookup",
                schema: "party",
                table: "contacts",
                columns: new[] { "institution_id", "party_id", "lookup_token" },
                unique: true,
                filter: "effective_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_party_names_institution_normalised",
                schema: "party",
                table: "names",
                columns: new[] { "institution_id", "normalised" });

            migrationBuilder.CreateIndex(
                name: "ix_party_names_institution_party_effective",
                schema: "party",
                table: "names",
                columns: new[] { "institution_id", "party_id", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "ix_parties_institution_status_updated",
                schema: "party",
                table: "parties",
                columns: new[] { "institution_id", "status", "updated_at" });

            // A captured revision is evidence: it may be appended but never rewritten, even by a
            // principal that holds update rights on the table. Decision: epic 03 append-only history.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION onboarding.reject_application_revision_change() RETURNS trigger
                    LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION 'onboarding.application_revisions is append-only (revision %)', OLD.revision
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER trg_application_revisions_append_only
                    BEFORE UPDATE OR DELETE ON onboarding.application_revisions
                    FOR EACH ROW EXECUTE FUNCTION onboarding.reject_application_revision_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_application_revisions_append_only ON onboarding.application_revisions;
                DROP FUNCTION IF EXISTS onboarding.reject_application_revision_change();
                """);

            migrationBuilder.DropTable(
                name: "addresses",
                schema: "party");

            migrationBuilder.DropTable(
                name: "application_revisions",
                schema: "onboarding");

            migrationBuilder.DropTable(
                name: "contacts",
                schema: "party");

            migrationBuilder.DropTable(
                name: "individuals",
                schema: "party");

            migrationBuilder.DropTable(
                name: "names",
                schema: "party");

            migrationBuilder.DropTable(
                name: "applications",
                schema: "onboarding");

            migrationBuilder.DropTable(
                name: "parties",
                schema: "party");
        }
    }
}
