using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomersAndClassifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_number_sequences",
                schema: "customer",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_number_sequences", x => x.institution_id);
                    table.CheckConstraint("ck_customer_number_sequences_next_value", "next_value >= 1");
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "customer",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    party_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    customer_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    branch_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => new { x.institution_id, x.customer_id });
                    table.CheckConstraint("ck_customers_concurrency_version", "concurrency_version > 0");
                    table.CheckConstraint("ck_customers_customer_number", "char_length(customer_number) > 0");
                    table.CheckConstraint("ck_customers_status", "status IN ('pending', 'active', 'restricted', 'dormant', 'deceased', 'closed', 'rejected')");
                    table.ForeignKey(
                        name: "FK_customers_parties_institution_id_party_id",
                        columns: x => new { x.institution_id, x.party_id },
                        principalSchema: "party",
                        principalTable: "parties",
                        principalColumns: new[] { "institution_id", "party_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_classifications",
                schema: "customer",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    tier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    segment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    risk_class = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requires_enhanced_due_diligence = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    workflow_decision_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_classifications", x => new { x.institution_id, x.customer_id, x.version });
                    table.CheckConstraint("ck_customer_classifications_approved", "status <> 'approved' OR (workflow_decision_reference IS NOT NULL AND approved_at IS NOT NULL)");
                    table.CheckConstraint("ck_customer_classifications_status", "status IN ('proposed', 'approved', 'superseded')");
                    table.CheckConstraint("ck_customer_classifications_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_customer_classifications_customers_institution_id_customer_~",
                        columns: x => new { x.institution_id, x.customer_id },
                        principalSchema: "customer",
                        principalTable: "customers",
                        principalColumns: new[] { "institution_id", "customer_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_status_history",
                schema: "customer",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    from_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    to_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    approval_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_status_history", x => new { x.institution_id, x.customer_id, x.sequence });
                    table.CheckConstraint("ck_customer_status_history_from", "from_status IN ('pending', 'active', 'restricted', 'dormant')");
                    table.CheckConstraint("ck_customer_status_history_sequence", "sequence >= 1");
                    table.CheckConstraint("ck_customer_status_history_to", "to_status IN ('active', 'restricted', 'dormant', 'deceased', 'closed', 'rejected')");
                    table.ForeignKey(
                        name: "FK_customer_status_history_customers_institution_id_customer_id",
                        columns: x => new { x.institution_id, x.customer_id },
                        principalSchema: "customer",
                        principalTable: "customers",
                        principalColumns: new[] { "institution_id", "customer_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_customer_classifications_approved",
                schema: "customer",
                table: "customer_classifications",
                columns: new[] { "institution_id", "customer_id" },
                unique: true,
                filter: "status = 'approved'");

            migrationBuilder.CreateIndex(
                name: "ix_customers_institution_status_updated",
                schema: "customer",
                table: "customers",
                columns: new[] { "institution_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_customers_number",
                schema: "customer",
                table: "customers",
                columns: new[] { "institution_id", "customer_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_customers_party",
                schema: "customer",
                table: "customers",
                columns: new[] { "institution_id", "party_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_classifications",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "customer_number_sequences",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "customer_status_history",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "customer");
        }
    }
}
