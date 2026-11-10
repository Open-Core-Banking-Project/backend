using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountOpeningClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_opening_claims",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    claim_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    limit_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    limit_key_value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_application_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    acquired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_opening_claims", x => new { x.institution_id, x.claim_id });
                    table.ForeignKey(
                        name: "FK_account_opening_claims_account_applications_institution_id_~",
                        columns: x => new { x.institution_id, x.account_application_id },
                        principalSchema: "account",
                        principalTable: "account_applications",
                        principalColumns: new[] { "institution_id", "account_application_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_opening_claims_institution_id_account_application_id",
                schema: "account",
                table: "account_opening_claims",
                columns: new[] { "institution_id", "account_application_id" });

            migrationBuilder.CreateIndex(
                name: "IX_account_opening_claims_institution_id_customer_id_limit_key~",
                schema: "account",
                table: "account_opening_claims",
                columns: new[] { "institution_id", "customer_id", "limit_key", "limit_key_value" },
                unique: true,
                filter: "released_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_opening_claims",
                schema: "account");
        }
    }
}
