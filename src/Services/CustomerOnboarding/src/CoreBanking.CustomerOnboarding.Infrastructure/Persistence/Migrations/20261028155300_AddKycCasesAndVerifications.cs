using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKycCasesAndVerifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_verifications",
                schema: "kyc",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    verification_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    case_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    application_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    party_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requirement_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    check_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requirement_snapshot_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    provider_mapping_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_verifications", x => new { x.institution_id, x.verification_id });
                    table.CheckConstraint("ck_identity_verifications_outcome", "outcome IS NULL OR outcome IN ('passed', 'failed', 'inconclusive', 'manual_review')");
                    table.CheckConstraint("ck_identity_verifications_status", "status IN ('created', 'submitted', 'pending', 'succeeded', 'failed', 'uncertain', 'manual_review', 'cancelled')");
                    table.CheckConstraint("ck_identity_verifications_succeeded", "status <> 'succeeded' OR (outcome IS NOT NULL AND provider_mapping_version IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "kyc_cases",
                schema: "kyc",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    case_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    application_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    party_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requirement_snapshot_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kyc_cases", x => new { x.institution_id, x.case_id });
                    table.CheckConstraint("ck_kyc_cases_completed", "(status = 'completed') = (completed_at IS NOT NULL)");
                    table.CheckConstraint("ck_kyc_cases_status", "status IN ('open', 'completed')");
                    table.ForeignKey(
                        name: "FK_kyc_cases_applications_institution_id_application_id",
                        columns: x => new { x.institution_id, x.application_id },
                        principalSchema: "onboarding",
                        principalTable: "applications",
                        principalColumns: new[] { "institution_id", "application_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "kyc_requirement_history",
                schema: "kyc",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    case_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requirement_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kyc_requirement_history", x => new { x.institution_id, x.case_id, x.requirement_code, x.sequence });
                    table.CheckConstraint("ck_kyc_requirement_history_sequence", "sequence >= 1");
                });

            migrationBuilder.CreateTable(
                name: "verification_status_history",
                schema: "kyc",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    verification_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    recorded_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_status_history", x => new { x.institution_id, x.verification_id, x.sequence });
                    table.CheckConstraint("ck_verification_status_history_outcome", "outcome IS NULL OR outcome IN ('passed', 'failed', 'inconclusive', 'manual_review')");
                    table.CheckConstraint("ck_verification_status_history_sequence", "sequence >= 1");
                });

            migrationBuilder.CreateTable(
                name: "verification_attempts",
                schema: "kyc",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    verification_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attempt_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_attempts", x => new { x.institution_id, x.verification_id, x.attempt_id });
                    table.CheckConstraint("ck_verification_attempts_status", "status IN ('submitted', 'pending', 'succeeded', 'failed', 'uncertain', 'cancelled_before_submission')");
                    table.ForeignKey(
                        name: "FK_verification_attempts_identity_verifications_institution_id~",
                        columns: x => new { x.institution_id, x.verification_id },
                        principalSchema: "kyc",
                        principalTable: "identity_verifications",
                        principalColumns: new[] { "institution_id", "verification_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "kyc_requirements",
                schema: "kyc",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    case_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requirement_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    linked_verification_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    waiver_authority = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    waiver_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    waived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kyc_requirements", x => new { x.institution_id, x.case_id, x.requirement_code });
                    table.CheckConstraint("ck_kyc_requirements_status", "status IN ('required', 'evidence_pending', 'ready', 'in_progress', 'passed', 'failed', 'inconclusive', 'manual_review', 'waived')");
                    table.CheckConstraint("ck_kyc_requirements_waiver", "(status = 'waived') = (waived_at IS NOT NULL AND waiver_authority IS NOT NULL AND waiver_reason IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_kyc_requirements_kyc_cases_institution_id_case_id",
                        columns: x => new { x.institution_id, x.case_id },
                        principalSchema: "kyc",
                        principalTable: "kyc_cases",
                        principalColumns: new[] { "institution_id", "case_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_identity_verifications_active_requirement",
                schema: "kyc",
                table: "identity_verifications",
                columns: new[] { "institution_id", "application_id", "requirement_code" },
                unique: true,
                filter: "status NOT IN ('succeeded', 'failed', 'cancelled')");

            migrationBuilder.CreateIndex(
                name: "ux_kyc_cases_application",
                schema: "kyc",
                table: "kyc_cases",
                columns: new[] { "institution_id", "application_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kyc_requirement_history",
                schema: "kyc");

            migrationBuilder.DropTable(
                name: "kyc_requirements",
                schema: "kyc");

            migrationBuilder.DropTable(
                name: "verification_attempts",
                schema: "kyc");

            migrationBuilder.DropTable(
                name: "verification_status_history",
                schema: "kyc");

            migrationBuilder.DropTable(
                name: "kyc_cases",
                schema: "kyc");

            migrationBuilder.DropTable(
                name: "identity_verifications",
                schema: "kyc");
        }
    }
}
