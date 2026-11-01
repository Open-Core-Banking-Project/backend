using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustedDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trusted_devices",
                schema: "security",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    device_id = table.Column<string>(type: "text", nullable: false),
                    identity_id = table.Column<string>(type: "text", nullable: false),
                    key_thumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    display_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    trust_state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    trusted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trust_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    step_up_evidence_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trusted_devices", x => new { x.institution_id, x.device_id });
                    table.CheckConstraint("ck_trusted_devices_revoked", "trust_state <> 'Revoked' OR (revoked_at IS NOT NULL AND revocation_reason IS NOT NULL)");
                    table.CheckConstraint("ck_trusted_devices_state", "trust_state IN ('PendingStepUp', 'Trusted', 'Suspended', 'Revoked')");
                    table.CheckConstraint("ck_trusted_devices_trust_period", "trust_expires_at IS NULL OR trusted_at IS NULL OR trust_expires_at > trusted_at");
                });

            migrationBuilder.CreateIndex(
                name: "ix_trusted_devices_identity",
                schema: "security",
                table: "trusted_devices",
                columns: new[] { "institution_id", "identity_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_trusted_devices_trusted_key",
                schema: "security",
                table: "trusted_devices",
                columns: new[] { "institution_id", "identity_id", "key_thumbprint" },
                unique: true,
                filter: "trust_state = 'Trusted'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trusted_devices",
                schema: "security");
        }
    }
}
