using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountStatementRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_statement_requests",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: false),
                    period_to = table.Column<DateOnly>(type: "date", nullable: false),
                    format_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    purpose_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    policy_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    requester_type_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requester_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ledger_query_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    integrity_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    refusal_codes = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    artifact_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    artifact_ready_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retention_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    legal_hold = table.Column<bool>(type: "boolean", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_statement_requests", x => new { x.institution_id, x.request_id });
                    table.CheckConstraint("ck_account_statement_requests_format", "format_code IN ('pdf', 'csv')");
                    table.CheckConstraint("ck_account_statement_requests_period", "period_to >= period_from");
                    table.CheckConstraint("ck_account_statement_requests_status", "status IN ('refused', 'awaiting_artifact', 'ready', 'expired')");
                    table.CheckConstraint("ck_account_statement_requests_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_account_statement_requests_accounts_institution_id_account_~",
                        columns: x => new { x.institution_id, x.account_id },
                        principalSchema: "account",
                        principalTable: "accounts",
                        principalColumns: new[] { "institution_id", "account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_statement_artifact_references",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reference_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    artifact_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    requester_type_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requester_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    access_count = table.Column<int>(type: "integer", nullable: false),
                    last_accessed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_statement_artifact_references", x => new { x.institution_id, x.reference_id });
                    table.CheckConstraint("ck_account_statement_artifact_references_expiry", "expires_at > issued_at");
                    table.ForeignKey(
                        name: "FK_account_statement_artifact_references_account_statement_req~",
                        columns: x => new { x.institution_id, x.request_id },
                        principalSchema: "account",
                        principalTable: "account_statement_requests",
                        principalColumns: new[] { "institution_id", "request_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_statement_artifact_references_institution_id_reques~",
                schema: "account",
                table: "account_statement_artifact_references",
                columns: new[] { "institution_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_statement_requests_institution_id_account_id_reques~",
                schema: "account",
                table: "account_statement_requests",
                columns: new[] { "institution_id", "account_id", "requested_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_statement_artifact_references",
                schema: "account");

            migrationBuilder.DropTable(
                name: "account_statement_requests",
                schema: "account");
        }
    }
}
