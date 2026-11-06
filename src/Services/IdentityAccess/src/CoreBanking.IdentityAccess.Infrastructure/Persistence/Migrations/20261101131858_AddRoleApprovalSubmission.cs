using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleApprovalSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approval_content_hash",
                schema: "access",
                table: "roles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approval_request_id",
                schema: "access",
                table: "roles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "submitted_at",
                schema: "access",
                table: "roles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "submitted_by",
                schema: "access",
                table: "roles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_roles_approval_submission_complete",
                schema: "access",
                table: "roles",
                sql: "(approval_request_id IS NULL AND approval_content_hash IS NULL AND submitted_by IS NULL AND submitted_at IS NULL) OR (approval_request_id IS NOT NULL AND approval_content_hash IS NOT NULL AND submitted_by IS NOT NULL AND submitted_at IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_roles_institution_id_approval_request_id",
                schema: "access",
                table: "roles",
                columns: new[] { "institution_id", "approval_request_id" },
                unique: true,
                filter: "approval_request_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_roles_approval_submission_complete",
                schema: "access",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "IX_roles_institution_id_approval_request_id",
                schema: "access",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "approval_content_hash",
                schema: "access",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "approval_request_id",
                schema: "access",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                schema: "access",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "submitted_by",
                schema: "access",
                table: "roles");
        }
    }
}
