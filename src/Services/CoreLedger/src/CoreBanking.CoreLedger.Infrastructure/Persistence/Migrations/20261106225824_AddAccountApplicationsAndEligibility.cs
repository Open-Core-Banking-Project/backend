using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountApplicationsAndEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "account");

            migrationBuilder.CreateTable(
                name: "account_applications",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_application_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    product_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    product_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    product_family = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    product_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    product_version_number = table.Column<int>(type: "integer", nullable: false),
                    product_content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    product_effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    product_effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    branch_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    account_purpose_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ownership_type_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_applications", x => new { x.institution_id, x.account_application_id });
                    table.CheckConstraint("ck_account_applications_status", "status IN ('Draft', 'EligibilityPending', 'Eligible', 'Ineligible', 'PendingApproval', 'Approved', 'Rejected', 'CancelledBeforeActivation', 'Open', 'Active')");
                    table.CheckConstraint("ck_account_applications_version", "version > 0");
                });

            migrationBuilder.CreateTable(
                name: "account_eligibility_evaluations",
                schema: "account",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    evaluation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    account_application_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    product_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    product_content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    customer_facts_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    branch_reference_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    currency_reference_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    facts_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    eligible = table.Column<bool>(type: "boolean", nullable: false),
                    facts_current = table.Column<bool>(type: "boolean", nullable: false),
                    decision_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    trace_json = table.Column<string>(type: "jsonb", nullable: false),
                    limit_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    product_maximum = table.Column<int>(type: "integer", nullable: true),
                    counted_holdings = table.Column<int>(type: "integer", nullable: true),
                    effective_maximum = table.Column<int>(type: "integer", nullable: true),
                    counted_holding_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    policy_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    evaluated_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    evaluated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_eligibility_evaluations", x => new { x.institution_id, x.evaluation_id });
                    table.ForeignKey(
                        name: "FK_account_eligibility_evaluations_account_applications_instit~",
                        columns: x => new { x.institution_id, x.account_application_id },
                        principalSchema: "account",
                        principalTable: "account_applications",
                        principalColumns: new[] { "institution_id", "account_application_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_applications_institution_id_customer_id_status",
                schema: "account",
                table: "account_applications",
                columns: new[] { "institution_id", "customer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_account_applications_institution_id_product_id_status",
                schema: "account",
                table: "account_applications",
                columns: new[] { "institution_id", "product_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_account_eligibility_evaluations_institution_id_account_appl~",
                schema: "account",
                table: "account_eligibility_evaluations",
                columns: new[] { "institution_id", "account_application_id", "evaluated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_eligibility_evaluations",
                schema: "account");

            migrationBuilder.DropTable(
                name: "account_applications",
                schema: "account");
        }
    }
}
