using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_corrections",
                schema: "customer",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    correction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    field_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    prior_value_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    new_value_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    requires_reverification = table.Column<bool>(type: "boolean", nullable: false),
                    requirement_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    approval_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    recorded_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_corrections", x => new { x.institution_id, x.correction_id });
                    table.CheckConstraint("ck_customer_corrections_changed", "prior_value_reference <> new_value_reference");
                    table.CheckConstraint("ck_customer_corrections_reason", "length(btrim(reason)) >= 3");
                    table.CheckConstraint("ck_customer_corrections_reverification", "NOT requires_reverification OR requirement_code IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_customer_corrections_customers_institution_id_customer_id",
                        columns: x => new { x.institution_id, x.customer_id },
                        principalSchema: "customer",
                        principalTable: "customers",
                        principalColumns: new[] { "institution_id", "customer_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_corrections_customer",
                schema: "customer",
                table: "customer_corrections",
                columns: new[] { "institution_id", "customer_id", "recorded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_corrections",
                schema: "customer");
        }
    }
}
