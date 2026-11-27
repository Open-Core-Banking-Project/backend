using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManualJournals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "manual_journal_approvals",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    manual_journal_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    decision_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    approver_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    permission = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_finance_head = table.Column<bool>(type: "boolean", nullable: false),
                    entitlement_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manual_journal_approvals", x => new { x.institution_id, x.manual_journal_id, x.decision_id });
                });

            migrationBuilder.CreateTable(
                name: "manual_journal_lines",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    manual_journal_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    ledger_account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    chart_account_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    account_role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    account_class = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    component = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requires_compensating_account = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manual_journal_lines", x => new { x.institution_id, x.manual_journal_id, x.sequence });
                    table.CheckConstraint("ck_manual_journal_lines_amount", "amount > 0");
                    table.CheckConstraint("ck_manual_journal_lines_component", "component IN ('principal', 'fee', 'tax', 'commission', 'penalty', 'interest', 'waiver', 'rounding')");
                    table.CheckConstraint("ck_manual_journal_lines_direction", "direction IN ('debit', 'credit')");
                    table.CheckConstraint("ck_manual_journal_lines_sequence", "sequence > 0");
                });

            migrationBuilder.CreateTable(
                name: "manual_journal_requests",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    manual_journal_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    legal_entity_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    policy_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    policy_content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    required_approvers = table.Column<int>(type: "integer", nullable: false),
                    requires_finance_head = table.Column<bool>(type: "boolean", nullable: false),
                    finance_head_permission = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    maker_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    approved_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    workflow_request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    financial_transaction_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    refusal_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    break_glass = table.Column<bool>(type: "boolean", nullable: false),
                    break_glass_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    dual_control_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    security_evidence_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    break_glass_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manual_journal_requests", x => new { x.institution_id, x.manual_journal_id });
                    table.CheckConstraint("ck_manual_journal_requests_amount", "total_amount > 0");
                    table.CheckConstraint("ck_manual_journal_requests_approvers", "required_approvers BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_manual_journal_requests_break_glass", "(break_glass = false AND break_glass_actor_id IS NULL AND dual_control_actor_id IS NULL AND security_evidence_reference IS NULL AND break_glass_valid_until IS NULL) OR (break_glass = true AND break_glass_actor_id IS NOT NULL AND dual_control_actor_id IS NOT NULL AND security_evidence_reference IS NOT NULL AND break_glass_valid_until IS NOT NULL AND break_glass_actor_id <> dual_control_actor_id)");
                    table.CheckConstraint("ck_manual_journal_requests_hash", "request_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_manual_journal_requests_posted", "(status = 'posted' AND posted_at IS NOT NULL AND financial_transaction_id IS NOT NULL) OR (status <> 'posted' AND posted_at IS NULL AND financial_transaction_id IS NULL)");
                    table.CheckConstraint("ck_manual_journal_requests_status", "status IN ('draft', 'submitted', 'approved', 'rejected', 'posted')");
                    table.CheckConstraint("ck_manual_journal_requests_version", "version > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_manual_journal_approvals_institution_id_manual_journal_id_a~",
                schema: "ledger",
                table: "manual_journal_approvals",
                columns: new[] { "institution_id", "manual_journal_id", "approver_actor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_manual_journal_requests_institution_id_request_id",
                schema: "ledger",
                table: "manual_journal_requests",
                columns: new[] { "institution_id", "request_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION ledger.guard_manual_journal_request()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $guard$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'manual journal evidence cannot be deleted'
                            USING ERRCODE = '55000';
                    END IF;

                    IF OLD.status = 'posted' THEN
                        RAISE EXCEPTION 'a posted manual journal is immutable'
                            USING ERRCODE = '55000';
                    END IF;

                    IF NEW.request_id <> OLD.request_id
                        OR NEW.legal_entity_code <> OLD.legal_entity_code
                        OR NEW.currency_code <> OLD.currency_code
                        OR NEW.total_amount <> OLD.total_amount
                        OR NEW.reason_code <> OLD.reason_code
                        OR NEW.evidence_reference <> OLD.evidence_reference
                        OR NEW.request_hash <> OLD.request_hash
                        OR NEW.policy_version_id <> OLD.policy_version_id
                        OR NEW.policy_content_hash <> OLD.policy_content_hash
                        OR NEW.required_approvers <> OLD.required_approvers
                        OR NEW.requires_finance_head <> OLD.requires_finance_head
                        OR NEW.finance_head_permission <> OLD.finance_head_permission
                        OR NEW.maker_actor_id <> OLD.maker_actor_id
                        OR NEW.break_glass <> OLD.break_glass
                    THEN
                        RAISE EXCEPTION 'manual journal content is immutable; submit a new request'
                            USING ERRCODE = '55000';
                    END IF;

                    RETURN NEW;
                END
                $guard$;

                CREATE TRIGGER guard_manual_journal_request
                BEFORE UPDATE OR DELETE ON ledger.manual_journal_requests
                FOR EACH ROW EXECUTE FUNCTION ledger.guard_manual_journal_request();

                CREATE OR REPLACE FUNCTION ledger.reject_manual_journal_child_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $guard$
                BEGIN
                    RAISE EXCEPTION 'manual journal lines and approvals are append-only'
                        USING ERRCODE = '55000';
                END
                $guard$;

                CREATE TRIGGER reject_manual_journal_line_mutation
                BEFORE UPDATE OR DELETE ON ledger.manual_journal_lines
                FOR EACH ROW EXECUTE FUNCTION ledger.reject_manual_journal_child_mutation();

                CREATE TRIGGER reject_manual_journal_approval_mutation
                BEFORE UPDATE OR DELETE ON ledger.manual_journal_approvals
                FOR EACH ROW EXECUTE FUNCTION ledger.reject_manual_journal_child_mutation();

                REVOKE ALL ON FUNCTION ledger.guard_manual_journal_request() FROM PUBLIC;
                REVOKE ALL ON FUNCTION ledger.reject_manual_journal_child_mutation() FROM PUBLIC;

                DO $privileges$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'coreledger_app') THEN
                        RAISE NOTICE 'The coreledger_app role does not exist; manual journal privileges were not installed.';
                        RETURN;
                    END IF;

                    GRANT SELECT, INSERT, UPDATE ON ledger.manual_journal_requests TO coreledger_app;
                    GRANT SELECT, INSERT ON ledger.manual_journal_lines TO coreledger_app;
                    GRANT SELECT, INSERT ON ledger.manual_journal_approvals TO coreledger_app;
                    GRANT EXECUTE ON FUNCTION ledger.guard_manual_journal_request() TO coreledger_app;
                    GRANT EXECUTE ON FUNCTION ledger.reject_manual_journal_child_mutation() TO coreledger_app;
                END
                $privileges$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS guard_manual_journal_request ON ledger.manual_journal_requests;
                DROP TRIGGER IF EXISTS reject_manual_journal_line_mutation ON ledger.manual_journal_lines;
                DROP TRIGGER IF EXISTS reject_manual_journal_approval_mutation ON ledger.manual_journal_approvals;
                DROP FUNCTION IF EXISTS ledger.guard_manual_journal_request();
                DROP FUNCTION IF EXISTS ledger.reject_manual_journal_child_mutation();
                """);

            migrationBuilder.DropTable(
                name: "manual_journal_approvals",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "manual_journal_lines",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "manual_journal_requests",
                schema: "ledger");
        }
    }
}
