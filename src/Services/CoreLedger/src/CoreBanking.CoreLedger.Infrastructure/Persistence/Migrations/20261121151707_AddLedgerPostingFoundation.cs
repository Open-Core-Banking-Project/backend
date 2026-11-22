using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerPostingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ledger");

            migrationBuilder.CreateTable(
                name: "financial_transactions",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    financial_transaction_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    transaction_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    source_service = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    source_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    external_reference_producer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    external_reference_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    external_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, collation: "C"),
                    event_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    transaction_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_transactions", x => new { x.institution_id, x.financial_transaction_id });
                    table.CheckConstraint("ck_financial_transactions_external_reference", "(external_reference_producer IS NULL AND external_reference_type IS NULL AND external_reference IS NULL) OR (external_reference_producer IS NOT NULL AND external_reference_type IS NOT NULL AND external_reference IS NOT NULL)");
                    table.CheckConstraint("ck_financial_transactions_external_reference_type", "external_reference_type IS NULL OR external_reference_type IN ('provider_reference', 'channel_reference', 'settlement_reference', 'statement_reference', 'external_reference', 'internal_reference')");
                    table.CheckConstraint("ck_financial_transactions_posted_at", "(status = 'posted' AND posted_at IS NOT NULL) OR (status <> 'posted' AND posted_at IS NULL)");
                    table.CheckConstraint("ck_financial_transactions_request_hash", "request_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_financial_transactions_status", "status IN ('received', 'pending_authorisation', 'ready_to_post', 'posted', 'rejected', 'failed_before_commit', 'disputed', 'reversed')");
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    journal_entry_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    financial_transaction_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    journal_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    mapping_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    legal_entity_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    total_debits = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_credits = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    transaction_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_entries", x => new { x.institution_id, x.journal_entry_id });
                    table.CheckConstraint("ck_journal_entries_balanced_totals", "total_debits > 0 AND total_debits = total_credits");
                    table.CheckConstraint("ck_journal_entries_status", "status = 'posted'");
                    table.ForeignKey(
                        name: "FK_journal_entries_financial_transactions_institution_id_finan~",
                        columns: x => new { x.institution_id, x.financial_transaction_id },
                        principalSchema: "ledger",
                        principalTable: "financial_transactions",
                        principalColumns: new[] { "institution_id", "financial_transaction_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transaction_status_history",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    history_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    financial_transaction_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_status_history", x => new { x.institution_id, x.history_id });
                    table.CheckConstraint("ck_transaction_status_history_sequence", "sequence > 0");
                    table.CheckConstraint("ck_transaction_status_history_status", "status IN ('received', 'pending_authorisation', 'ready_to_post', 'posted', 'rejected', 'failed_before_commit', 'disputed', 'reversed')");
                    table.ForeignKey(
                        name: "FK_transaction_status_history_financial_transactions_instituti~",
                        columns: x => new { x.institution_id, x.financial_transaction_id },
                        principalSchema: "ledger",
                        principalTable: "financial_transactions",
                        principalColumns: new[] { "institution_id", "financial_transaction_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_lines",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    journal_line_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    journal_entry_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    ledger_account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    legal_entity_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    component_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_lines", x => new { x.institution_id, x.journal_line_id });
                    table.CheckConstraint("ck_journal_lines_amount", "amount > 0");
                    table.CheckConstraint("ck_journal_lines_component", "component_type IN ('principal', 'fee', 'tax', 'commission', 'penalty', 'interest', 'waiver', 'rounding')");
                    table.CheckConstraint("ck_journal_lines_direction", "direction IN ('debit', 'credit')");
                    table.CheckConstraint("ck_journal_lines_reason", "component_type = 'principal' OR reason_code IS NOT NULL");
                    table.CheckConstraint("ck_journal_lines_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "FK_journal_lines_journal_entries_institution_id_journal_entry_~",
                        columns: x => new { x.institution_id, x.journal_entry_id },
                        principalSchema: "ledger",
                        principalTable: "journal_entries",
                        principalColumns: new[] { "institution_id", "journal_entry_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_transactions_institution_id_external_reference_pr~",
                schema: "ledger",
                table: "financial_transactions",
                columns: new[] { "institution_id", "external_reference_producer", "external_reference_type", "external_reference" },
                unique: true,
                filter: "external_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_financial_transactions_institution_id_source_service_source~",
                schema: "ledger",
                table: "financial_transactions",
                columns: new[] { "institution_id", "source_service", "source_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_transactions_institution_id_transaction_reference",
                schema: "ledger",
                table: "financial_transactions",
                columns: new[] { "institution_id", "transaction_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_institution_id_financial_transaction_id",
                schema: "ledger",
                table: "journal_entries",
                columns: new[] { "institution_id", "financial_transaction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_institution_id_journal_reference",
                schema: "ledger",
                table: "journal_entries",
                columns: new[] { "institution_id", "journal_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_institution_id_journal_entry_id_sequence",
                schema: "ledger",
                table: "journal_lines",
                columns: new[] { "institution_id", "journal_entry_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transaction_status_history_institution_id_financial_transac~",
                schema: "ledger",
                table: "transaction_status_history",
                columns: new[] { "institution_id", "financial_transaction_id", "sequence" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION ledger.assert_posted_journal_balanced()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $guard$
                DECLARE
                    target_institution text := COALESCE(NEW.institution_id, OLD.institution_id);
                    target_journal text := COALESCE(NEW.journal_entry_id, OLD.journal_entry_id);
                    header ledger.journal_entries%ROWTYPE;
                    line_count integer;
                    debit_total numeric(19,4);
                    credit_total numeric(19,4);
                    wrong_scope_count integer;
                BEGIN
                    SELECT * INTO header
                    FROM ledger.journal_entries
                    WHERE institution_id = target_institution
                      AND journal_entry_id = target_journal;

                    IF NOT FOUND THEN
                        RETURN NULL;
                    END IF;

                    SELECT
                        count(*),
                        COALESCE(sum(amount) FILTER (WHERE direction = 'debit'), 0),
                        COALESCE(sum(amount) FILTER (WHERE direction = 'credit'), 0),
                        count(*) FILTER (
                            WHERE legal_entity_code <> header.legal_entity_code
                               OR currency_code <> header.currency_code)
                    INTO line_count, debit_total, credit_total, wrong_scope_count
                    FROM ledger.journal_lines
                    WHERE institution_id = target_institution
                      AND journal_entry_id = target_journal;

                    IF line_count < 2
                       OR wrong_scope_count <> 0
                       OR debit_total <> credit_total
                       OR debit_total <> header.total_debits
                       OR credit_total <> header.total_credits THEN
                        RAISE EXCEPTION 'posted journal % is incomplete, crosses scope, or is unbalanced', target_journal
                            USING ERRCODE = '23514';
                    END IF;

                    RETURN NULL;
                END
                $guard$;

                CREATE CONSTRAINT TRIGGER ck_journal_entries_balanced_at_commit
                AFTER INSERT OR UPDATE ON ledger.journal_entries
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION ledger.assert_posted_journal_balanced();

                CREATE CONSTRAINT TRIGGER ck_journal_lines_balanced_at_commit
                AFTER INSERT OR UPDATE OR DELETE ON ledger.journal_lines
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION ledger.assert_posted_journal_balanced();

                CREATE OR REPLACE FUNCTION ledger.assert_posted_transaction_has_journal()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $guard$
                BEGIN
                    IF NEW.status = 'posted'
                       AND NOT EXISTS (
                           SELECT 1
                           FROM ledger.journal_entries
                           WHERE institution_id = NEW.institution_id
                             AND financial_transaction_id = NEW.financial_transaction_id) THEN
                        RAISE EXCEPTION 'posted financial transaction % has no journal', NEW.financial_transaction_id
                            USING ERRCODE = '23514';
                    END IF;

                    RETURN NULL;
                END
                $guard$;

                CREATE CONSTRAINT TRIGGER ck_financial_transaction_has_journal_at_commit
                AFTER INSERT OR UPDATE ON ledger.financial_transactions
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION ledger.assert_posted_transaction_has_journal();

                CREATE OR REPLACE FUNCTION ledger.reject_posted_financial_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $guard$
                BEGIN
                    IF TG_TABLE_NAME IN ('journal_entries', 'journal_lines', 'transaction_status_history') THEN
                        RAISE EXCEPTION 'ledger financial evidence is append-only'
                            USING ERRCODE = '55000';
                    END IF;

                    IF TG_TABLE_NAME = 'financial_transactions' AND OLD.status = 'posted' THEN
                        RAISE EXCEPTION 'posted financial transaction is immutable'
                            USING ERRCODE = '55000';
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;

                    RETURN NEW;
                END
                $guard$;

                CREATE TRIGGER reject_journal_entry_mutation
                BEFORE UPDATE OR DELETE ON ledger.journal_entries
                FOR EACH ROW EXECUTE FUNCTION ledger.reject_posted_financial_mutation();

                CREATE TRIGGER reject_journal_line_mutation
                BEFORE UPDATE OR DELETE ON ledger.journal_lines
                FOR EACH ROW EXECUTE FUNCTION ledger.reject_posted_financial_mutation();

                CREATE TRIGGER reject_transaction_history_mutation
                BEFORE UPDATE OR DELETE ON ledger.transaction_status_history
                FOR EACH ROW EXECUTE FUNCTION ledger.reject_posted_financial_mutation();

                CREATE TRIGGER reject_posted_transaction_mutation
                BEFORE UPDATE OR DELETE ON ledger.financial_transactions
                FOR EACH ROW EXECUTE FUNCTION ledger.reject_posted_financial_mutation();

                REVOKE ALL ON SCHEMA ledger FROM PUBLIC;
                REVOKE ALL ON FUNCTION ledger.assert_posted_journal_balanced() FROM PUBLIC;
                REVOKE ALL ON FUNCTION ledger.assert_posted_transaction_has_journal() FROM PUBLIC;
                REVOKE ALL ON FUNCTION ledger.reject_posted_financial_mutation() FROM PUBLIC;

                DO $privileges$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'coreledger_app') THEN
                        RAISE NOTICE 'The coreledger_app role does not exist; application privileges for the ledger schema were not installed.';
                        RETURN;
                    END IF;

                    GRANT USAGE ON SCHEMA ledger TO coreledger_app;
                    GRANT SELECT, INSERT, UPDATE, DELETE ON ledger.financial_transactions TO coreledger_app;
                    GRANT SELECT, INSERT ON ledger.journal_entries, ledger.journal_lines, ledger.transaction_status_history TO coreledger_app;
                    GRANT EXECUTE ON FUNCTION ledger.assert_posted_journal_balanced() TO coreledger_app;
                    GRANT EXECUTE ON FUNCTION ledger.assert_posted_transaction_has_journal() TO coreledger_app;
                    GRANT EXECUTE ON FUNCTION ledger.reject_posted_financial_mutation() TO coreledger_app;

                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA ledger
                        GRANT SELECT, INSERT ON TABLES TO coreledger_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA ledger
                        GRANT EXECUTE ON FUNCTIONS TO coreledger_app;
                END
                $privileges$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $privileges$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'coreledger_app') THEN
                        ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA ledger
                            REVOKE SELECT, INSERT ON TABLES FROM coreledger_app;
                        ALTER DEFAULT PRIVILEGES FOR ROLE coreledger_migrator IN SCHEMA ledger
                            REVOKE EXECUTE ON FUNCTIONS FROM coreledger_app;
                    END IF;
                END
                $privileges$;

                DROP FUNCTION IF EXISTS ledger.reject_posted_financial_mutation() CASCADE;
                DROP FUNCTION IF EXISTS ledger.assert_posted_transaction_has_journal() CASCADE;
                DROP FUNCTION IF EXISTS ledger.assert_posted_journal_balanced() CASCADE;
                """);

            migrationBuilder.DropTable(
                name: "journal_lines",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "transaction_status_history",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "journal_entries",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "financial_transactions",
                schema: "ledger");
        }
    }
}
