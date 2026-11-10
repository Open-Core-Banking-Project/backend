using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountApprovalAndLedgerAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ledger_account_reference",
                schema: "account",
                table: "accounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approval_request_id",
                schema: "account",
                table: "account_applications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                schema: "account",
                table: "account_applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approved_by_actor_id",
                schema: "account",
                table: "account_applications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "eligibility_evaluation_id",
                schema: "account",
                table: "account_applications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "eligibility_snapshot_hash",
                schema: "account",
                table: "account_applications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ledger_account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_accounts", x => new { x.institution_id, x.ledger_account_id });
                    table.CheckConstraint("ck_ledger_accounts_status", "status IN ('initialised', 'closed')");
                    table.ForeignKey(
                        name: "FK_ledger_accounts_accounts_institution_id_account_id",
                        columns: x => new { x.institution_id, x.account_id },
                        principalSchema: "account",
                        principalTable: "accounts",
                        principalColumns: new[] { "institution_id", "account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_institution_id_account_id",
                schema: "account",
                table: "ledger_accounts",
                columns: new[] { "institution_id", "account_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ledger_accounts",
                schema: "account");

            migrationBuilder.DropColumn(
                name: "ledger_account_reference",
                schema: "account",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "approval_request_id",
                schema: "account",
                table: "account_applications");

            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "account",
                table: "account_applications");

            migrationBuilder.DropColumn(
                name: "approved_by_actor_id",
                schema: "account",
                table: "account_applications");

            migrationBuilder.DropColumn(
                name: "eligibility_evaluation_id",
                schema: "account",
                table: "account_applications");

            migrationBuilder.DropColumn(
                name: "eligibility_snapshot_hash",
                schema: "account",
                table: "account_applications");
        }
    }
}
