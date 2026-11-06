using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaEvidenceInvalidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "invalidated_at",
                schema: "security",
                table: "step_up_evidence",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invalidation_reason_code",
                schema: "security",
                table: "step_up_evidence",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_step_up_invalidation_after_issue",
                schema: "security",
                table: "step_up_evidence",
                sql: "invalidated_at IS NULL OR invalidated_at >= issued_at");

            migrationBuilder.AddCheckConstraint(
                name: "ck_step_up_invalidation_reason",
                schema: "security",
                table: "step_up_evidence",
                sql: "(invalidated_at IS NULL AND invalidation_reason_code IS NULL) OR (invalidated_at IS NOT NULL AND invalidation_reason_code IN ('user_requested', 'device_lost', 'suspected_compromise'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_step_up_invalidation_after_issue",
                schema: "security",
                table: "step_up_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_step_up_invalidation_reason",
                schema: "security",
                table: "step_up_evidence");

            migrationBuilder.DropColumn(
                name: "invalidated_at",
                schema: "security",
                table: "step_up_evidence");

            migrationBuilder.DropColumn(
                name: "invalidation_reason_code",
                schema: "security",
                table: "step_up_evidence");
        }
    }
}
