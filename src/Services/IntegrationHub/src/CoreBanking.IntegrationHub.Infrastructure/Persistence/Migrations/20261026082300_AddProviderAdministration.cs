using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IntegrationHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "partner");

            migrationBuilder.EnsureSchema(
                name: "provider");

            migrationBuilder.CreateTable(
                name: "api_clients",
                schema: "partner",
                columns: table => new
                {
                    api_client_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    client_identifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    allowed_scopes_json = table.Column<string>(type: "jsonb", nullable: false),
                    allowed_network_ranges_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_clients", x => x.api_client_id);
                    table.CheckConstraint("ck_api_clients_status", "status IN ('active', 'revoked')");
                });

            migrationBuilder.CreateTable(
                name: "credential_references",
                schema: "provider",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    credential_reference_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reference_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    owner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    previous_record_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credential_references", x => x.record_id);
                    table.CheckConstraint("ck_credential_references_status", "status IN ('active', 'superseded', 'revoked')");
                    table.CheckConstraint("ck_credential_references_version", "version > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_clients_institution_id_environment_client_identifier",
                schema: "partner",
                table: "api_clients",
                columns: new[] { "institution_id", "environment", "client_identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credential_references_institution_id_credential_reference_i~",
                schema: "provider",
                table: "credential_references",
                columns: new[] { "institution_id", "credential_reference_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credential_references_institution_id_environment_provider_c~",
                schema: "provider",
                table: "credential_references",
                columns: new[] { "institution_id", "environment", "provider_code", "reference_name", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_clients",
                schema: "partner");

            migrationBuilder.DropTable(
                name: "credential_references",
                schema: "provider");
        }
    }
}
