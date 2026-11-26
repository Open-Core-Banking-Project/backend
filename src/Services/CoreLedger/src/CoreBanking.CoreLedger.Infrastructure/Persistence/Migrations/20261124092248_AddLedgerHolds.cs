using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "holds",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    hold_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ledger_account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_service = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    purpose_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    remaining_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    captured_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    released_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_holds", x => new { x.institution_id, x.hold_id });
                    table.CheckConstraint("ck_holds_amounts", "original_amount > 0 AND remaining_amount >= 0 AND captured_amount >= 0 AND released_amount >= 0 AND remaining_amount + captured_amount + released_amount = original_amount");
                    table.CheckConstraint("ck_holds_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_holds_status", "status IN ('active', 'partially_captured', 'captured', 'released', 'expired')");
                    table.CheckConstraint("ck_holds_version", "version > 0");
                    table.ForeignKey(
                        name: "FK_holds_ledger_accounts_institution_id_ledger_account_id",
                        columns: x => new { x.institution_id, x.ledger_account_id },
                        principalSchema: "account",
                        principalTable: "ledger_accounts",
                        principalColumns: new[] { "institution_id", "ledger_account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hold_status_history",
                schema: "ledger",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    history_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    hold_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    transition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    remaining_after = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    financial_transaction_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    reason_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hold_status_history", x => new { x.institution_id, x.history_id });
                    table.CheckConstraint("ck_hold_history_amounts", "amount >= 0 AND remaining_after >= 0");
                    table.CheckConstraint("ck_hold_history_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "FK_hold_status_history_holds_institution_id_hold_id",
                        columns: x => new { x.institution_id, x.hold_id },
                        principalSchema: "ledger",
                        principalTable: "holds",
                        principalColumns: new[] { "institution_id", "hold_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION ledger.guard_hold_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $guard$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'ledger holds cannot be deleted'
                            USING ERRCODE = '55000';
                    END IF;

                    IF OLD.status IN ('captured', 'released', 'expired') THEN
                        RAISE EXCEPTION 'terminal ledger holds are immutable'
                            USING ERRCODE = '55000';
                    END IF;

                    IF NEW.institution_id <> OLD.institution_id
                       OR NEW.hold_id <> OLD.hold_id
                       OR NEW.ledger_account_id <> OLD.ledger_account_id
                       OR NEW.source_service <> OLD.source_service
                       OR NEW.source_reference <> OLD.source_reference
                       OR NEW.purpose_code <> OLD.purpose_code
                       OR NEW.currency_code <> OLD.currency_code
                       OR NEW.original_amount <> OLD.original_amount
                       OR NEW.expires_at <> OLD.expires_at
                       OR NEW.created_at <> OLD.created_at THEN
                        RAISE EXCEPTION 'immutable ledger hold fields cannot change'
                            USING ERRCODE = '55000';
                    END IF;

                    IF NEW.version <> OLD.version + 1
                       OR NEW.remaining_amount > OLD.remaining_amount
                       OR NEW.captured_amount < OLD.captured_amount
                       OR NEW.released_amount < OLD.released_amount THEN
                        RAISE EXCEPTION 'invalid ledger hold lifecycle transition'
                            USING ERRCODE = '55000';
                    END IF;

                    RETURN NEW;
                END
                $guard$;

                CREATE TRIGGER guard_ledger_hold_mutation
                BEFORE UPDATE OR DELETE ON ledger.holds
                FOR EACH ROW EXECUTE FUNCTION ledger.guard_hold_mutation();

                CREATE OR REPLACE FUNCTION ledger.reject_hold_history_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $guard$
                BEGIN
                    RAISE EXCEPTION 'ledger hold history is append-only'
                        USING ERRCODE = '55000';
                END
                $guard$;

                CREATE TRIGGER reject_ledger_hold_history_mutation
                BEFORE UPDATE OR DELETE ON ledger.hold_status_history
                FOR EACH ROW EXECUTE FUNCTION ledger.reject_hold_history_mutation();

                REVOKE ALL ON FUNCTION ledger.guard_hold_mutation() FROM PUBLIC;
                REVOKE ALL ON FUNCTION ledger.reject_hold_history_mutation() FROM PUBLIC;

                DO $privileges$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'coreledger_app') THEN
                        RAISE NOTICE 'The coreledger_app role does not exist; ledger hold privileges were not installed.';
                        RETURN;
                    END IF;

                    REVOKE DELETE ON ledger.holds FROM coreledger_app;
                    REVOKE UPDATE, DELETE ON ledger.hold_status_history FROM coreledger_app;
                    GRANT SELECT, INSERT, UPDATE ON ledger.holds TO coreledger_app;
                    GRANT SELECT, INSERT ON ledger.hold_status_history TO coreledger_app;
                    GRANT EXECUTE ON FUNCTION ledger.guard_hold_mutation() TO coreledger_app;
                    GRANT EXECUTE ON FUNCTION ledger.reject_hold_history_mutation() TO coreledger_app;
                END
                $privileges$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_hold_status_history_institution_id_hold_id_sequence",
                schema: "ledger",
                table: "hold_status_history",
                columns: new[] { "institution_id", "hold_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_holds_institution_id_ledger_account_id_status_expires_at",
                schema: "ledger",
                table: "holds",
                columns: new[] { "institution_id", "ledger_account_id", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_holds_institution_id_source_service_source_reference",
                schema: "ledger",
                table: "holds",
                columns: new[] { "institution_id", "source_service", "source_reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS ledger.reject_hold_history_mutation() CASCADE;
                DROP FUNCTION IF EXISTS ledger.guard_hold_mutation() CASCADE;
                """);

            migrationBuilder.DropTable(
                name: "hold_status_history",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "holds",
                schema: "ledger");
        }
    }
}
