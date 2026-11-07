using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountLiens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_liens",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    lien_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    priority_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    priority_rank = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    outstanding_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    authority_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_liens", x => new { x.institution_id, x.lien_id });
                    table.CheckConstraint("ck_account_liens_amount", "amount > 0");
                    table.CheckConstraint("ck_account_liens_outstanding", "outstanding_amount >= 0 AND outstanding_amount <= amount");
                    table.CheckConstraint("ck_account_liens_period", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("ck_account_liens_priority", "priority_code IN ('statutory_first', 'tax', 'institution', 'customer')");
                    table.CheckConstraint("ck_account_liens_rank", "priority_rank >= 0");
                    table.CheckConstraint("ck_account_liens_status", "status IN ('active', 'released')");
                    table.ForeignKey(
                        name: "FK_account_liens_accounts_institution_id_account_id",
                        columns: x => new { x.institution_id, x.account_id },
                        principalSchema: "account",
                        principalTable: "accounts",
                        principalColumns: new[] { "institution_id", "account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_lien_movements",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    movement_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    lien_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    movement_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    outstanding_after = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    authority_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_lien_movements", x => new { x.institution_id, x.movement_id });
                    table.CheckConstraint("ck_account_lien_movements_amount", "amount > 0");
                    table.CheckConstraint("ck_account_lien_movements_outstanding", "outstanding_after >= 0");
                    table.CheckConstraint("ck_account_lien_movements_type", "movement_type IN ('partial_release', 'full_release', 'satisfaction')");
                    table.ForeignKey(
                        name: "FK_account_lien_movements_account_liens_institution_id_lien_id",
                        columns: x => new { x.institution_id, x.lien_id },
                        principalSchema: "account",
                        principalTable: "account_liens",
                        principalColumns: new[] { "institution_id", "lien_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_lien_movements_institution_id_lien_id_occurred_at",
                schema: "account",
                table: "account_lien_movements",
                columns: new[] { "institution_id", "lien_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_account_liens_institution_id_account_id_status",
                schema: "account",
                table: "account_liens",
                columns: new[] { "institution_id", "account_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_lien_movements",
                schema: "account");

            migrationBuilder.DropTable(
                name: "account_liens",
                schema: "account");
        }
    }
}
