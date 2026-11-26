using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_period_history",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    legal_entity_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    period_code = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    from_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    to_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    approval_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    allowed_event_codes_json = table.Column<string>(type: "text", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_period_history", x => new { x.institution_id, x.legal_entity_code, x.period_code, x.sequence });
                    table.CheckConstraint("ck_accounting_period_history_sequence", "sequence > 0");
                    table.CheckConstraint("ck_accounting_period_history_status", "from_status IN ('open', 'soft_closed', 'closed', 'locked', 'reopened') AND to_status IN ('open', 'soft_closed', 'closed', 'locked', 'reopened')");
                });

            migrationBuilder.CreateTable(
                name: "accounting_periods",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    legal_entity_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    period_code = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    allowed_event_codes_json = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    closed_debit_total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    closed_credit_total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    close_summary_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    close_approval_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reopen_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reopen_reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reclosed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reclose_reconciliation_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lock_approval_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_periods", x => new { x.institution_id, x.legal_entity_code, x.period_code });
                    table.CheckConstraint("ck_accounting_periods_closed_evidence", "status NOT IN ('closed', 'locked') OR (closed_at IS NOT NULL AND close_summary_hash IS NOT NULL AND closed_debit_total IS NOT NULL AND closed_credit_total IS NOT NULL AND closed_debit_total = closed_credit_total)");
                    table.CheckConstraint("ck_accounting_periods_locked_evidence", "status <> 'locked' OR locked_at IS NOT NULL");
                    table.CheckConstraint("ck_accounting_periods_reopen_evidence", "status <> 'reopened' OR (reopen_expires_at IS NOT NULL AND reopen_reason_code IS NOT NULL)");
                    table.CheckConstraint("ck_accounting_periods_status", "status IN ('open', 'soft_closed', 'closed', 'locked', 'reopened')");
                    table.CheckConstraint("ck_accounting_periods_version", "version > 0");
                });

            migrationBuilder.CreateTable(
                name: "recalculation_handoffs",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    handoff_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    legal_entity_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    period_code = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    financial_transaction_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    age_days = table.Column<int>(type: "integer", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    approval_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recalculation_handoffs", x => new { x.institution_id, x.handoff_id });
                    table.CheckConstraint("ck_recalculation_handoffs_age", "age_days > 90");
                });

            migrationBuilder.CreateIndex(
                name: "IX_recalculation_handoffs_institution_id_financial_transaction~",
                schema: "ledger",
                table: "recalculation_handoffs",
                columns: new[] { "institution_id", "financial_transaction_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION ledger.reject_accounting_period_evidence_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $guard$
                BEGIN
                    RAISE EXCEPTION 'accounting period evidence is append-only'
                        USING ERRCODE = '55000';
                END
                $guard$;

                CREATE TRIGGER reject_accounting_period_history_mutation
                BEFORE UPDATE OR DELETE ON ledger.accounting_period_history
                FOR EACH ROW EXECUTE FUNCTION ledger.reject_accounting_period_evidence_mutation();

                CREATE TRIGGER reject_recalculation_handoff_mutation
                BEFORE UPDATE OR DELETE ON ledger.recalculation_handoffs
                FOR EACH ROW EXECUTE FUNCTION ledger.reject_accounting_period_evidence_mutation();

                CREATE OR REPLACE FUNCTION ledger.guard_accounting_period_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $guard$
                BEGIN
                    RAISE EXCEPTION 'an accounting period cannot be deleted'
                        USING ERRCODE = '55000';
                END
                $guard$;

                CREATE TRIGGER guard_accounting_period_mutation
                BEFORE DELETE ON ledger.accounting_periods
                FOR EACH ROW EXECUTE FUNCTION ledger.guard_accounting_period_mutation();

                REVOKE ALL ON FUNCTION ledger.reject_accounting_period_evidence_mutation() FROM PUBLIC;
                REVOKE ALL ON FUNCTION ledger.guard_accounting_period_mutation() FROM PUBLIC;

                DO $privileges$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'coreledger_app') THEN
                        RAISE NOTICE 'The coreledger_app role does not exist; accounting period privileges were not installed.';
                        RETURN;
                    END IF;

                    GRANT SELECT, INSERT, UPDATE ON ledger.accounting_periods TO coreledger_app;
                    GRANT SELECT, INSERT ON ledger.accounting_period_history TO coreledger_app;
                    GRANT SELECT, INSERT ON ledger.recalculation_handoffs TO coreledger_app;
                    GRANT EXECUTE ON FUNCTION ledger.reject_accounting_period_evidence_mutation() TO coreledger_app;
                    GRANT EXECUTE ON FUNCTION ledger.guard_accounting_period_mutation() TO coreledger_app;
                END
                $privileges$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS guard_accounting_period_mutation ON ledger.accounting_periods;
                DROP TRIGGER IF EXISTS reject_accounting_period_history_mutation ON ledger.accounting_period_history;
                DROP TRIGGER IF EXISTS reject_recalculation_handoff_mutation ON ledger.recalculation_handoffs;
                DROP FUNCTION IF EXISTS ledger.guard_accounting_period_mutation();
                DROP FUNCTION IF EXISTS ledger.reject_accounting_period_evidence_mutation();
                """);

            migrationBuilder.DropTable(
                name: "accounting_period_history",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "accounting_periods",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "recalculation_handoffs",
                schema: "ledger");
        }
    }
}
