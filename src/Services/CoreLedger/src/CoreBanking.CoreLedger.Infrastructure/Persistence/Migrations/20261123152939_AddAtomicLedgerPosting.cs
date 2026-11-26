using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAtomicLedgerPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "normal_side",
                schema: "account",
                table: "ledger_accounts",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "account_balances",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ledger_account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    normal_side = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    debit_total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    credit_total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    net_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    credit_limit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    hold_remaining = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    pending_authorised = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_balances", x => new { x.institution_id, x.ledger_account_id });
                    table.CheckConstraint("ck_account_balances_deductions", "credit_limit >= 0 AND hold_remaining >= 0 AND pending_authorised >= 0");
                    table.CheckConstraint("ck_account_balances_net", "net_balance = debit_total - credit_total");
                    table.CheckConstraint("ck_account_balances_normal_side", "normal_side IN ('debit', 'credit')");
                    table.CheckConstraint("ck_account_balances_totals", "debit_total >= 0 AND credit_total >= 0");
                    table.CheckConstraint("ck_account_balances_version", "version > 0");
                    table.ForeignKey(
                        name: "FK_account_balances_ledger_accounts_institution_id_ledger_acco~",
                        columns: x => new { x.institution_id, x.ledger_account_id },
                        principalSchema: "account",
                        principalTable: "ledger_accounts",
                        principalColumns: new[] { "institution_id", "ledger_account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_movements",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    movement_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    journal_line_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ledger_account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    signed_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    visible_balance_before = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    visible_balance_after = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    debit_total_after = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    credit_total_after = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    balance_version = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_movements", x => new { x.institution_id, x.movement_id });
                    table.CheckConstraint("ck_account_movements_amount", "amount > 0");
                    table.CheckConstraint("ck_account_movements_direction", "direction IN ('debit', 'credit')");
                    table.CheckConstraint("ck_account_movements_signed_amount", "(direction = 'debit' AND signed_amount = amount) OR (direction = 'credit' AND signed_amount = -amount)");
                    table.CheckConstraint("ck_account_movements_totals", "debit_total_after >= 0 AND credit_total_after >= 0");
                    table.CheckConstraint("ck_account_movements_version", "balance_version > 1");
                    table.ForeignKey(
                        name: "FK_account_movements_account_balances_institution_id_ledger_ac~",
                        columns: x => new { x.institution_id, x.ledger_account_id },
                        principalSchema: "ledger",
                        principalTable: "account_balances",
                        principalColumns: new[] { "institution_id", "ledger_account_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_movements_journal_lines_institution_id_journal_line~",
                        columns: x => new { x.institution_id, x.journal_line_id },
                        principalSchema: "ledger",
                        principalTable: "journal_lines",
                        principalColumns: new[] { "institution_id", "journal_line_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_accounts_normal_side",
                schema: "account",
                table: "ledger_accounts",
                sql: "normal_side IS NULL OR normal_side IN ('debit', 'credit')");

            migrationBuilder.Sql(
                """
                -- Every pre-existing ledger account is a customer-deposit account. D05 fixes that
                -- role as credit-normal; later GL roles must be initialised with their chart side.
                UPDATE account.ledger_accounts
                SET normal_side = 'credit'
                WHERE normal_side IS NULL;

                INSERT INTO ledger.account_balances
                    (institution_id, ledger_account_id, currency_code, normal_side,
                     debit_total, credit_total, net_balance, credit_limit, hold_remaining,
                     pending_authorised, version, as_of)
                SELECT institution_id, ledger_account_id, currency_code, normal_side,
                       0, 0, 0, 0, 0, 0, 1, created_at
                FROM account.ledger_accounts;

                CREATE OR REPLACE FUNCTION ledger.reject_movement_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $guard$
                BEGIN
                    RAISE EXCEPTION 'ledger movements are append-only'
                        USING ERRCODE = '55000';
                END
                $guard$;

                CREATE TRIGGER reject_account_movement_mutation
                BEFORE UPDATE OR DELETE ON ledger.account_movements
                FOR EACH ROW EXECUTE FUNCTION ledger.reject_movement_mutation();

                CREATE OR REPLACE FUNCTION ledger.guard_balance_delete()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $guard$
                BEGIN
                    IF OLD.debit_total <> 0 OR OLD.credit_total <> 0 OR OLD.net_balance <> 0
                       OR OLD.credit_limit <> 0 OR OLD.hold_remaining <> 0 OR OLD.pending_authorised <> 0
                       OR EXISTS (
                           SELECT 1 FROM ledger.account_movements
                           WHERE institution_id = OLD.institution_id
                             AND ledger_account_id = OLD.ledger_account_id) THEN
                        RAISE EXCEPTION 'an authoritative or used ledger balance cannot be deleted'
                            USING ERRCODE = '55000';
                    END IF;

                    RETURN OLD;
                END
                $guard$;

                CREATE TRIGGER guard_account_balance_delete
                BEFORE DELETE ON ledger.account_balances
                FOR EACH ROW EXECUTE FUNCTION ledger.guard_balance_delete();

                REVOKE ALL ON FUNCTION ledger.reject_movement_mutation() FROM PUBLIC;
                REVOKE ALL ON FUNCTION ledger.guard_balance_delete() FROM PUBLIC;

                DO $privileges$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'coreledger_app') THEN
                        RAISE NOTICE 'The coreledger_app role does not exist; atomic posting privileges were not installed.';
                        RETURN;
                    END IF;

                    GRANT SELECT, INSERT, UPDATE, DELETE ON ledger.account_balances TO coreledger_app;
                    GRANT SELECT, INSERT ON ledger.account_movements TO coreledger_app;
                    GRANT EXECUTE ON FUNCTION ledger.reject_movement_mutation() TO coreledger_app;
                    GRANT EXECUTE ON FUNCTION ledger.guard_balance_delete() TO coreledger_app;
                END
                $privileges$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_account_movements_institution_id_journal_line_id",
                schema: "ledger",
                table: "account_movements",
                columns: new[] { "institution_id", "journal_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_movements_institution_id_ledger_account_id_balance_~",
                schema: "ledger",
                table: "account_movements",
                columns: new[] { "institution_id", "ledger_account_id", "balance_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS ledger.guard_balance_delete() CASCADE;
                DROP FUNCTION IF EXISTS ledger.reject_movement_mutation() CASCADE;
                """);

            migrationBuilder.DropTable(
                name: "account_movements",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "account_balances",
                schema: "ledger");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_accounts_normal_side",
                schema: "account",
                table: "ledger_accounts");

            migrationBuilder.DropColumn(
                name: "normal_side",
                schema: "account",
                table: "ledger_accounts");
        }
    }
}
