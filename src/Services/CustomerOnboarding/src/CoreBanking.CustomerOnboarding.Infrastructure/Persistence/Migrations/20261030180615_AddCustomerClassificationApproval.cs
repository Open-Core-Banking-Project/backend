using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerClassificationApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approval_content_hash",
                schema: "customer",
                table: "customer_classifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approval_request_id",
                schema: "customer",
                table: "customer_classifications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "submitted_for_approval_at",
                schema: "customer",
                table: "customer_classifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_classifications_approval_hash",
                schema: "customer",
                table: "customer_classifications",
                sql: "approval_content_hash IS NULL OR length(approval_content_hash) = 64");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_classifications_approval_hash",
                schema: "customer",
                table: "customer_classifications");

            migrationBuilder.DropColumn(
                name: "approval_content_hash",
                schema: "customer",
                table: "customer_classifications");

            migrationBuilder.DropColumn(
                name: "approval_request_id",
                schema: "customer",
                table: "customer_classifications");

            migrationBuilder.DropColumn(
                name: "submitted_for_approval_at",
                schema: "customer",
                table: "customer_classifications");
        }
    }
}
