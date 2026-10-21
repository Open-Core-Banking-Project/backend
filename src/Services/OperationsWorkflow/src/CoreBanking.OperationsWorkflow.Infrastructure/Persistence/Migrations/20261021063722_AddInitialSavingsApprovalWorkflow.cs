using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.OperationsWorkflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInitialSavingsApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workflow");

            migrationBuilder.CreateTable(
                name: "approval_requests",
                schema: "workflow",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_request_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    workflow_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    workflow_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    workflow_definition_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_service = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    subject_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    subject_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    subject_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    maker_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    required_permission = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_requests", x => new { x.institution_id, x.request_id });
                    table.CheckConstraint("ck_approval_requests_status", "status IN ('submitted', 'approved', 'rejected')");
                });

            migrationBuilder.CreateTable(
                name: "approval_decisions",
                schema: "workflow",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    decision_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    subject_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    subject_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    approver_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    permission = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entitlement_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entitlement_valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entitlement_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_decisions", x => new { x.institution_id, x.decision_id });
                    table.CheckConstraint("ck_approval_decisions_decision", "decision IN ('approved', 'rejected')");
                    table.CheckConstraint("ck_approval_decisions_entitlement_period", "entitlement_valid_until > entitlement_valid_from");
                    table.CheckConstraint("ck_approval_decisions_rejection_reason", "decision <> 'rejected' OR reason IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_approval_decisions_approval_requests_institution_id_request~",
                        columns: x => new { x.institution_id, x.request_id },
                        principalSchema: "workflow",
                        principalTable: "approval_requests",
                        principalColumns: new[] { "institution_id", "request_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_approval_decisions_institution_id_request_id",
                schema: "workflow",
                table: "approval_decisions",
                columns: new[] { "institution_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_approval_requests_institution_id_source_service_source_requ~",
                schema: "workflow",
                table: "approval_requests",
                columns: new[] { "institution_id", "source_service", "source_request_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_approval_requests_institution_id_subject_type_subject_id_su~",
                schema: "workflow",
                table: "approval_requests",
                columns: new[] { "institution_id", "subject_type", "subject_id", "subject_version", "subject_hash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_decisions",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "approval_requests",
                schema: "workflow");
        }
    }
}
