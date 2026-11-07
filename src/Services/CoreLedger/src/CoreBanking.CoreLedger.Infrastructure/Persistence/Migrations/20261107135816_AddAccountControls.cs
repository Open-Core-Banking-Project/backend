using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_blocks",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    block_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    block_type_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    operation_scope_json = table.Column<string>(type: "jsonb", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    authority_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requires_manual_release = table.Column<bool>(type: "boolean", nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    applied_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    released_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    control_version = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_blocks", x => new { x.institution_id, x.block_id });
                    table.CheckConstraint("ck_account_blocks_period", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("ck_account_blocks_status", "status IN ('active', 'released', 'expired')");
                    table.ForeignKey(
                        name: "FK_account_blocks_accounts_institution_id_account_id",
                        columns: x => new { x.institution_id, x.account_id },
                        principalSchema: "account",
                        principalTable: "accounts",
                        principalColumns: new[] { "institution_id", "account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_freezes",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    freeze_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    freeze_type_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    authority_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requires_manual_release = table.Column<bool>(type: "boolean", nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    applied_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    released_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    control_version = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_freezes", x => new { x.institution_id, x.freeze_id });
                    table.CheckConstraint("ck_account_freezes_period", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("ck_account_freezes_status", "status IN ('active', 'released', 'expired')");
                    table.CheckConstraint("ck_account_freezes_type", "freeze_type_code IN ('regulatory', 'institution')");
                    table.ForeignKey(
                        name: "FK_account_freezes_accounts_institution_id_account_id",
                        columns: x => new { x.institution_id, x.account_id },
                        principalSchema: "account",
                        principalTable: "accounts",
                        principalColumns: new[] { "institution_id", "account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_restrictions",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    restriction_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    restriction_type_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    posting_direction_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    channel_scope_json = table.Column<string>(type: "jsonb", nullable: false),
                    transaction_type_scope_json = table.Column<string>(type: "jsonb", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    authority_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requires_manual_release = table.Column<bool>(type: "boolean", nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    applied_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    released_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    control_version = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_restrictions", x => new { x.institution_id, x.restriction_id });
                    table.CheckConstraint("ck_account_restrictions_direction", "posting_direction_code IN ('debit', 'credit', 'both')");
                    table.CheckConstraint("ck_account_restrictions_period", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("ck_account_restrictions_status", "status IN ('active', 'released', 'expired')");
                    table.ForeignKey(
                        name: "FK_account_restrictions_accounts_institution_id_account_id",
                        columns: x => new { x.institution_id, x.account_id },
                        principalSchema: "account",
                        principalTable: "accounts",
                        principalColumns: new[] { "institution_id", "account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_blocks_institution_id_account_id_status",
                schema: "account",
                table: "account_blocks",
                columns: new[] { "institution_id", "account_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_account_freezes_institution_id_account_id_status",
                schema: "account",
                table: "account_freezes",
                columns: new[] { "institution_id", "account_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_account_restrictions_institution_id_account_id_status",
                schema: "account",
                table: "account_restrictions",
                columns: new[] { "institution_id", "account_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_blocks",
                schema: "account");

            migrationBuilder.DropTable(
                name: "account_freezes",
                schema: "account");

            migrationBuilder.DropTable(
                name: "account_restrictions",
                schema: "account");
        }
    }
}
