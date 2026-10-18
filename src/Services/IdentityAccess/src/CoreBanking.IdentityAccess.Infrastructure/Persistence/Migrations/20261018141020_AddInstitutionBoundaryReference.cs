using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionBoundaryReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "institution_boundaries",
                schema: "platform",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provisioning_plan_version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    initialised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_institution_boundaries", x => x.institution_id);
                    table.CheckConstraint("ck_institution_boundaries_plan_version", "provisioning_plan_version > 0");
                    table.CheckConstraint("ck_institution_boundaries_status", "status IN ('provisioning', 'active', 'suspended')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_institution_boundaries_source_event_id",
                schema: "platform",
                table: "institution_boundaries",
                column: "source_event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "institution_boundaries",
                schema: "platform");
        }
    }
}
