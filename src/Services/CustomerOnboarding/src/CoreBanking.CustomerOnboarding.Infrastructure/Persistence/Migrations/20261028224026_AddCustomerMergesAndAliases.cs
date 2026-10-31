using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerMergesAndAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_aliases",
                schema: "customer",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    alias_customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    survivor_customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    merge_request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_aliases", x => new { x.institution_id, x.alias_customer_id });
                    table.CheckConstraint("ck_customer_aliases_distinct", "alias_customer_id <> survivor_customer_id");
                });

            migrationBuilder.CreateTable(
                name: "merge_requests",
                schema: "customer",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    merge_request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    survivor_customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    plan_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    required_consumers = table.Column<string>(type: "jsonb", nullable: false),
                    approval_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merge_requests", x => new { x.institution_id, x.merge_request_id });
                    table.CheckConstraint("ck_merge_requests_approved", "status NOT IN ('approved', 'applying', 'completed', 'reconciliation_required') OR approval_reference IS NOT NULL");
                    table.CheckConstraint("ck_merge_requests_distinct", "survivor_customer_id <> source_customer_id");
                    table.CheckConstraint("ck_merge_requests_plan_hash", "char_length(plan_hash) = 64");
                    table.CheckConstraint("ck_merge_requests_status", "status IN ('proposed', 'analysed', 'pending_approval', 'approved', 'applying', 'completed', 'reconciliation_required', 'rejected', 'withdrawn')");
                });

            migrationBuilder.CreateTable(
                name: "merge_consumer_status",
                schema: "customer",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    merge_request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    consumer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merge_consumer_status", x => new { x.institution_id, x.merge_request_id, x.consumer });
                    table.ForeignKey(
                        name: "FK_merge_consumer_status_merge_requests_institution_id_merge_r~",
                        columns: x => new { x.institution_id, x.merge_request_id },
                        principalSchema: "customer",
                        principalTable: "merge_requests",
                        principalColumns: new[] { "institution_id", "merge_request_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_aliases_survivor",
                schema: "customer",
                table: "customer_aliases",
                columns: new[] { "institution_id", "survivor_customer_id" });

            migrationBuilder.CreateIndex(
                name: "ux_merge_requests_active_source",
                schema: "customer",
                table: "merge_requests",
                columns: new[] { "institution_id", "source_customer_id" },
                unique: true,
                filter: "status NOT IN ('completed', 'rejected', 'withdrawn')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_aliases",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "merge_consumer_status",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "merge_requests",
                schema: "customer");
        }
    }
}
