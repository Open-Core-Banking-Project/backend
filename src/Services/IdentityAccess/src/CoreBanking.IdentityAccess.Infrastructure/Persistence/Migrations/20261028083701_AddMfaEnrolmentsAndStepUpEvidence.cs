using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaEnrolmentsAndStepUpEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mfa_enrolments",
                schema: "security",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    enrolment_id = table.Column<string>(type: "text", nullable: false),
                    identity_id = table.Column<string>(type: "text", nullable: false),
                    factor_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mfa_enrolments", x => new { x.institution_id, x.enrolment_id });
                });

            migrationBuilder.CreateTable(
                name: "step_up_evidence",
                schema: "security",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    evidence_id = table.Column<string>(type: "text", nullable: false),
                    identity_id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    device_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    purpose = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    factor_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_step_up_evidence", x => new { x.institution_id, x.evidence_id });
                    table.CheckConstraint("ck_step_up_expiry_after_issue", "expires_at > issued_at");
                });

            migrationBuilder.CreateIndex(
                name: "ux_mfa_enrolments_active_factor",
                schema: "security",
                table: "mfa_enrolments",
                columns: new[] { "institution_id", "identity_id", "factor_kind" },
                unique: true,
                filter: "status <> 'revoked'");

            migrationBuilder.CreateIndex(
                name: "ix_step_up_evidence_lookup",
                schema: "security",
                table: "step_up_evidence",
                columns: new[] { "institution_id", "identity_id", "session_id", "purpose" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mfa_enrolments",
                schema: "security");

            migrationBuilder.DropTable(
                name: "step_up_evidence",
                schema: "security");
        }
    }
}
