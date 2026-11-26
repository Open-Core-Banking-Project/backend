using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_reversals",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    financial_reversal_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    original_financial_transaction_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reversal_financial_transaction_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    original_journal_entry_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reversal_journal_entry_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    mapping_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    reversed_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    remaining_after = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    workflow_decision_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    maker_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    approver_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    original_value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reversal_value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_reversals", x => new { x.institution_id, x.financial_reversal_id });
                    table.CheckConstraint("ck_financial_reversals_amounts", "reversed_amount > 0 AND remaining_after >= 0");
                    table.CheckConstraint("ck_financial_reversals_distinct_transactions", "original_financial_transaction_id <> reversal_financial_transaction_id");
                    table.CheckConstraint("ck_financial_reversals_workflow_evidence", "(workflow_decision_id IS NULL AND maker_actor_id IS NULL AND approver_actor_id IS NULL) OR (workflow_decision_id IS NOT NULL AND maker_actor_id IS NOT NULL AND approver_actor_id IS NOT NULL AND maker_actor_id <> approver_actor_id)");
                    table.ForeignKey(
                        name: "FK_financial_reversals_financial_transactions_institution_id_o~",
                        columns: x => new { x.institution_id, x.original_financial_transaction_id },
                        principalSchema: "ledger",
                        principalTable: "financial_transactions",
                        principalColumns: new[] { "institution_id", "financial_transaction_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_reversals_financial_transactions_institution_id_r~",
                        columns: x => new { x.institution_id, x.reversal_financial_transaction_id },
                        principalSchema: "ledger",
                        principalTable: "financial_transactions",
                        principalColumns: new[] { "institution_id", "financial_transaction_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_reversals_institution_id_original_financial_trans~",
                schema: "ledger",
                table: "financial_reversals",
                columns: new[] { "institution_id", "original_financial_transaction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_reversals_institution_id_reversal_financial_trans~",
                schema: "ledger",
                table: "financial_reversals",
                columns: new[] { "institution_id", "reversal_financial_transaction_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION ledger.reject_financial_reversal_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $guard$
                BEGIN
                    RAISE EXCEPTION 'financial reversal evidence is append-only'
                        USING ERRCODE = '55000';
                END
                $guard$;

                CREATE TRIGGER reject_financial_reversal_mutation
                BEFORE UPDATE OR DELETE ON ledger.financial_reversals
                FOR EACH ROW EXECUTE FUNCTION ledger.reject_financial_reversal_mutation();

                REVOKE ALL ON FUNCTION ledger.reject_financial_reversal_mutation() FROM PUBLIC;

                DO $privileges$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'coreledger_app') THEN
                        RAISE NOTICE 'The coreledger_app role does not exist; financial reversal privileges were not installed.';
                        RETURN;
                    END IF;

                    GRANT SELECT, INSERT ON ledger.financial_reversals TO coreledger_app;
                    GRANT EXECUTE ON FUNCTION ledger.reject_financial_reversal_mutation() TO coreledger_app;
                END
                $privileges$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS reject_financial_reversal_mutation ON ledger.financial_reversals;
                DROP FUNCTION IF EXISTS ledger.reject_financial_reversal_mutation();
                """);

            migrationBuilder.DropTable(
                name: "financial_reversals",
                schema: "ledger");
        }
    }
}
