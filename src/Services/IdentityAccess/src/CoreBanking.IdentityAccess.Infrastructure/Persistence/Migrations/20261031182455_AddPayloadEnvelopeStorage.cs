using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayloadEnvelopeStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "envelope_replay_records",
                schema: "security",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    replay_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_envelope_replay_records", x => new { x.institution_id, x.purpose, x.replay_id });
                    table.CheckConstraint("ck_envelope_replay_window", "expires_at > claimed_at");
                });

            migrationBuilder.CreateTable(
                name: "key_metadata",
                schema: "security",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    key_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key_version = table.Column<int>(type: "integer", nullable: false),
                    purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    algorithm = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    public_key = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    not_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_key_metadata", x => new { x.institution_id, x.key_id, x.key_version });
                    table.CheckConstraint("ck_key_metadata_revoked", "state <> 'Revoked' OR (revoked_at IS NOT NULL AND revocation_reason IS NOT NULL)");
                    table.CheckConstraint("ck_key_metadata_state", "state IN ('Active', 'Retired', 'Revoked')");
                    table.CheckConstraint("ck_key_metadata_validity", "not_after IS NULL OR not_after > not_before");
                    table.CheckConstraint("ck_key_metadata_version", "key_version >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_envelope_replay_records_expiry",
                schema: "security",
                table: "envelope_replay_records",
                columns: new[] { "institution_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_key_metadata_purpose",
                schema: "security",
                table: "key_metadata",
                columns: new[] { "institution_id", "purpose", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "envelope_replay_records",
                schema: "security");

            migrationBuilder.DropTable(
                name: "key_metadata",
                schema: "security");
        }
    }
}
