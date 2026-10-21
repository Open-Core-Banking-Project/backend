using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.OperationsWorkflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceExactApprovalDecisionBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_approval_decisions_approval_requests_institution_id_request~",
                schema: "workflow",
                table: "approval_decisions");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_approval_requests_institution_id_request_id_subject_version~",
                schema: "workflow",
                table: "approval_requests",
                columns: new[] { "institution_id", "request_id", "subject_version", "subject_hash" });

            migrationBuilder.CreateIndex(
                name: "IX_approval_decisions_institution_id_request_id_subject_versio~",
                schema: "workflow",
                table: "approval_decisions",
                columns: new[] { "institution_id", "request_id", "subject_version", "subject_hash" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_approval_decisions_entitlement_current",
                schema: "workflow",
                table: "approval_decisions",
                sql: "decided_at >= entitlement_valid_from AND decided_at < entitlement_valid_until");

            migrationBuilder.AddForeignKey(
                name: "FK_approval_decisions_approval_requests_institution_id_request~",
                schema: "workflow",
                table: "approval_decisions",
                columns: new[] { "institution_id", "request_id", "subject_version", "subject_hash" },
                principalSchema: "workflow",
                principalTable: "approval_requests",
                principalColumns: new[] { "institution_id", "request_id", "subject_version", "subject_hash" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_approval_decisions_approval_requests_institution_id_request~",
                schema: "workflow",
                table: "approval_decisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_approval_requests_institution_id_request_id_subject_version~",
                schema: "workflow",
                table: "approval_requests");

            migrationBuilder.DropIndex(
                name: "IX_approval_decisions_institution_id_request_id_subject_versio~",
                schema: "workflow",
                table: "approval_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_approval_decisions_entitlement_current",
                schema: "workflow",
                table: "approval_decisions");

            migrationBuilder.AddForeignKey(
                name: "FK_approval_decisions_approval_requests_institution_id_request~",
                schema: "workflow",
                table: "approval_decisions",
                columns: new[] { "institution_id", "request_id" },
                principalSchema: "workflow",
                principalTable: "approval_requests",
                principalColumns: new[] { "institution_id", "request_id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
