using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerAccountClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "chart_version_id",
                schema: "account",
                table: "ledger_accounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gl_account_code",
                schema: "account",
                table: "ledger_accounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "legal_entity_code",
                schema: "account",
                table: "ledger_accounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "chart_version_id",
                schema: "account",
                table: "ledger_accounts");

            migrationBuilder.DropColumn(
                name: "gl_account_code",
                schema: "account",
                table: "ledger_accounts");

            migrationBuilder.DropColumn(
                name: "legal_entity_code",
                schema: "account",
                table: "ledger_accounts");
        }
    }
}
