using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.OperationsWorkflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalApplicationResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "approval");

            migrationBuilder.AddColumn<string>(
                name: "application_status",
                schema: "workflow",
                table: "approval_requests",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "applied_at",
                schema: "workflow",
                table: "approval_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "apply_failure_code",
                schema: "workflow",
                table: "approval_requests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "application_results",
                schema: "approval",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    result_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    decision_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_service = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    subject_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_results", x => new { x.institution_id, x.result_id });
                    table.CheckConstraint("ck_application_results_failure_code", "outcome <> 'apply_failed' OR failure_code IS NOT NULL");
                    table.CheckConstraint("ck_application_results_outcome", "outcome IN ('applied', 'rejected', 'apply_failed')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_results_institution_id_request_id",
                schema: "approval",
                table: "application_results",
                columns: new[] { "institution_id", "request_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_results",
                schema: "approval");

            migrationBuilder.DropColumn(
                name: "application_status",
                schema: "workflow",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "applied_at",
                schema: "workflow",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "apply_failure_code",
                schema: "workflow",
                table: "approval_requests");
        }
    }
}
