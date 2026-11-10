using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountDormancyAndClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_closure_proposals",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    proposal_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    supporting_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    condition_snapshot_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ledger_account_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ledger_snapshot_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    conditions_as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ledger_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    available_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    holds = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    outstanding_lien_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    pending_settlements = table.Column<int>(type: "integer", nullable: false),
                    lien_release_decision_recorded = table.Column<bool>(type: "boolean", nullable: false),
                    dispute_open = table.Column<bool>(type: "boolean", nullable: false),
                    notice_pending = table.Column<bool>(type: "boolean", nullable: false),
                    refusal_codes = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    approval_request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    approved_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_closure_proposals", x => new { x.institution_id, x.proposal_id });
                    table.CheckConstraint("ck_account_closure_proposals_status", "status IN ('conditions_met', 'conditions_failed', 'pending_approval', 'pending_closure', 'closed', 'rejected')");
                    table.CheckConstraint("ck_account_closure_proposals_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_account_closure_proposals_accounts_institution_id_account_id",
                        columns: x => new { x.institution_id, x.account_id },
                        principalSchema: "account",
                        principalTable: "accounts",
                        principalColumns: new[] { "institution_id", "account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_dormancy_assessments",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    assessment_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    rule_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    rule_content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    threshold_days = table.Column<int>(type: "integer", nullable: false),
                    notice_lead_days = table.Column<int>(type: "integer", nullable: false),
                    clock_origin_date = table.Column<DateOnly>(type: "date", nullable: false),
                    last_qualifying_activity_date = table.Column<DateOnly>(type: "date", nullable: true),
                    inactivity_days = table.Column<int>(type: "integer", nullable: false),
                    decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason_codes = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    transition_permitted = table.Column<bool>(type: "boolean", nullable: false),
                    previous_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    resulting_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    assessed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_dormancy_assessments", x => new { x.institution_id, x.assessment_id });
                    table.CheckConstraint("ck_account_dormancy_assessments_decision", "decision IN ('NotDormant', 'NoticeDue', 'Dormant', 'NotAssessable')");
                    table.CheckConstraint("ck_account_dormancy_assessments_lead", "notice_lead_days >= 0 AND notice_lead_days <= threshold_days");
                    table.CheckConstraint("ck_account_dormancy_assessments_threshold", "threshold_days >= 0");
                    table.ForeignKey(
                        name: "FK_account_dormancy_assessments_accounts_institution_id_accoun~",
                        columns: x => new { x.institution_id, x.account_id },
                        principalSchema: "account",
                        principalTable: "accounts",
                        principalColumns: new[] { "institution_id", "account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_dormancy_leases",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    lease_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    acquired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_dormancy_leases", x => new { x.institution_id, x.lease_id });
                    table.ForeignKey(
                        name: "FK_account_dormancy_leases_accounts_institution_id_account_id",
                        columns: x => new { x.institution_id, x.account_id },
                        principalSchema: "account",
                        principalTable: "accounts",
                        principalColumns: new[] { "institution_id", "account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_dormancy_notices",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    notice_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    rule_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    channel_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_dormancy_notices", x => new { x.institution_id, x.notice_id });
                    table.CheckConstraint("ck_account_dormancy_notices_status", "status IN ('recorded', 'failed')");
                    table.ForeignKey(
                        name: "FK_account_dormancy_notices_accounts_institution_id_account_id",
                        columns: x => new { x.institution_id, x.account_id },
                        principalSchema: "account",
                        principalTable: "accounts",
                        principalColumns: new[] { "institution_id", "account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_closure_proposals_institution_id_account_id_status",
                schema: "account",
                table: "account_closure_proposals",
                columns: new[] { "institution_id", "account_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_account_closure_proposals_institution_id_request_id",
                schema: "account",
                table: "account_closure_proposals",
                columns: new[] { "institution_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_dormancy_assessments_institution_id_account_id_busi~",
                schema: "account",
                table: "account_dormancy_assessments",
                columns: new[] { "institution_id", "account_id", "business_date", "rule_version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_dormancy_leases_institution_id_account_id",
                schema: "account",
                table: "account_dormancy_leases",
                columns: new[] { "institution_id", "account_id" },
                unique: true,
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_account_dormancy_notices_institution_id_account_id_rule_ver~",
                schema: "account",
                table: "account_dormancy_notices",
                columns: new[] { "institution_id", "account_id", "rule_version_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_closure_proposals",
                schema: "account");

            migrationBuilder.DropTable(
                name: "account_dormancy_assessments",
                schema: "account");

            migrationBuilder.DropTable(
                name: "account_dormancy_leases",
                schema: "account");

            migrationBuilder.DropTable(
                name: "account_dormancy_notices",
                schema: "account");
        }
    }
}
