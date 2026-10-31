using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPreferencesAndConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "privacy");

            migrationBuilder.CreateTable(
                name: "consents",
                schema: "privacy",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    consent_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    lawful_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    recorded_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consents", x => new { x.institution_id, x.consent_id });
                    table.CheckConstraint("ck_consents_permitted", "state <> 'permitted' OR lawful_basis IS NOT NULL");
                    table.CheckConstraint("ck_consents_state", "state IN ('permitted', 'denied')");
                    table.CheckConstraint("ck_consents_version", "version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "service_preferences",
                schema: "customer",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    preference_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    lawful_basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recorded_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_preferences", x => new { x.institution_id, x.customer_id, x.purpose, x.channel, x.version });
                    table.CheckConstraint("ck_service_preferences_period", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_service_preferences_permitted", "state <> 'permitted' OR lawful_basis IS NOT NULL");
                    table.CheckConstraint("ck_service_preferences_source", "source IN ('customer', 'officer', 'channel')");
                    table.CheckConstraint("ck_service_preferences_state", "state IN ('permitted', 'denied')");
                    table.CheckConstraint("ck_service_preferences_version", "version >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "ux_consents_customer_purpose_version",
                schema: "privacy",
                table: "consents",
                columns: new[] { "institution_id", "customer_id", "purpose", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_service_preferences_current",
                schema: "customer",
                table: "service_preferences",
                columns: new[] { "institution_id", "customer_id", "purpose", "channel" },
                unique: true,
                filter: "effective_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_service_preferences_preference_id",
                schema: "customer",
                table: "service_preferences",
                columns: new[] { "institution_id", "preference_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consents",
                schema: "privacy");

            migrationBuilder.DropTable(
                name: "service_preferences",
                schema: "customer");
        }
    }
}
