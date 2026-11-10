using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolePublicationDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                schema: "access",
                table: "roles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decided_by",
                schema: "access",
                table: "roles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision",
                schema: "access",
                table: "roles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                schema: "access",
                table: "roles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "workflow_decision_reference",
                schema: "access",
                table: "roles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_roles_publication_decision_complete",
                schema: "access",
                table: "roles",
                sql: "(workflow_decision_reference IS NULL AND decision IS NULL AND decided_by IS NULL AND decided_at IS NULL AND published_at IS NULL) OR (workflow_decision_reference IS NOT NULL AND decision IN ('approved', 'rejected') AND decided_by IS NOT NULL AND decided_at IS NOT NULL AND ((decision = 'approved' AND published_at IS NOT NULL) OR (decision = 'rejected' AND published_at IS NULL)))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_roles_publication_decision_complete",
                schema: "access",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "decided_at",
                schema: "access",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "decided_by",
                schema: "access",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "decision",
                schema: "access",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "published_at",
                schema: "access",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "workflow_decision_reference",
                schema: "access",
                table: "roles");
        }
    }
}
