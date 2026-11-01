using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRelationshipManagerAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "relationship_manager_assignments",
                schema: "customer",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    assignment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manager_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    scope_branch_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    previous_assignment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relationship_manager_assignments", x => new { x.institution_id, x.assignment_id });
                    table.CheckConstraint("ck_relationship_manager_assignments_manager", "length(btrim(manager_actor_id)) > 0");
                    table.CheckConstraint("ck_relationship_manager_assignments_period", "effective_to IS NULL OR effective_to >= effective_from");
                    table.ForeignKey(
                        name: "FK_relationship_manager_assignments_customers_institution_id_c~",
                        columns: x => new { x.institution_id, x.customer_id },
                        principalSchema: "customer",
                        principalTable: "customers",
                        principalColumns: new[] { "institution_id", "customer_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_relationship_manager_assignments_manager",
                schema: "customer",
                table: "relationship_manager_assignments",
                columns: new[] { "institution_id", "manager_actor_id" });

            migrationBuilder.CreateIndex(
                name: "ux_relationship_manager_assignments_current",
                schema: "customer",
                table: "relationship_manager_assignments",
                columns: new[] { "institution_id", "customer_id" },
                unique: true,
                filter: "effective_to IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "relationship_manager_assignments",
                schema: "customer");
        }
    }
}
