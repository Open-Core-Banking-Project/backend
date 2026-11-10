using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountsAndNumberReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_number_reservations",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    scheme_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    scheme_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false),
                    high_water_mark = table.Column<long>(type: "bigint", nullable: false),
                    capacity = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_number_reservations", x => new { x.institution_id, x.scheme_version_id });
                    table.CheckConstraint("ck_account_number_reservations_capacity", "capacity > 0");
                    table.CheckConstraint("ck_account_number_reservations_high_water", "high_water_mark >= 0");
                    table.CheckConstraint("ck_account_number_reservations_next", "next_value >= 1");
                    table.CheckConstraint("ck_account_number_reservations_order", "next_value >= high_water_mark AND high_water_mark <= capacity");
                });

            migrationBuilder.CreateTable(
                name: "account_number_retirements",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    retirement_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    scheme_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sequence_value = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    retired_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_number_retirements", x => new { x.institution_id, x.retirement_id });
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_application_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    product_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    product_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    product_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    product_content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    branch_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    account_purpose_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ownership_type_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    account_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    masked_account_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scheme_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scheme_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ledger_initialisation_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => new { x.institution_id, x.account_id });
                    table.CheckConstraint("ck_accounts_ledger_initialisation", "ledger_initialisation_status IN ('not_initialised', 'initialised', 'failed')");
                    table.CheckConstraint("ck_accounts_status", "status IN ('Open', 'Active', 'Restricted', 'Frozen', 'Dormant', 'PendingClosure', 'Closed')");
                    table.ForeignKey(
                        name: "FK_accounts_account_applications_institution_id_account_applic~",
                        columns: x => new { x.institution_id, x.account_application_id },
                        principalSchema: "account",
                        principalTable: "account_applications",
                        principalColumns: new[] { "institution_id", "account_application_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_status_history",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    history_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_status_history", x => new { x.institution_id, x.history_id });
                    table.ForeignKey(
                        name: "FK_account_status_history_accounts_institution_id_account_id",
                        columns: x => new { x.institution_id, x.account_id },
                        principalSchema: "account",
                        principalTable: "accounts",
                        principalColumns: new[] { "institution_id", "account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_number_retirements_institution_id_scheme_version_id~",
                schema: "account",
                table: "account_number_retirements",
                columns: new[] { "institution_id", "scheme_version_id", "sequence_value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_status_history_institution_id_account_id_occurred_at",
                schema: "account",
                table: "account_status_history",
                columns: new[] { "institution_id", "account_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_institution_id_account_application_id",
                schema: "account",
                table: "accounts",
                columns: new[] { "institution_id", "account_application_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_institution_id_account_number",
                schema: "account",
                table: "accounts",
                columns: new[] { "institution_id", "account_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_institution_id_customer_id_status",
                schema: "account",
                table: "accounts",
                columns: new[] { "institution_id", "customer_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_number_reservations",
                schema: "account");

            migrationBuilder.DropTable(
                name: "account_number_retirements",
                schema: "account");

            migrationBuilder.DropTable(
                name: "account_status_history",
                schema: "account");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "account");
        }
    }
}
