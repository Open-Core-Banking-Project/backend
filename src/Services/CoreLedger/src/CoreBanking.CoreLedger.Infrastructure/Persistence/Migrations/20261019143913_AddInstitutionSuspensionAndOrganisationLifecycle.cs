using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionSuspensionAndOrganisationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "suspended_by_actor_id",
                schema: "institution",
                table: "institutions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suspension_correlation_id",
                schema: "institution",
                table: "institutions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suspension_reason",
                schema: "institution",
                table: "institutions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "organisation_status_history",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    history_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    previous_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    new_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    changed_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organisation_status_history", x => new { x.institution_id, x.history_id });
                    table.CheckConstraint("ck_organisation_history_resource", "resource_type IN ('region', 'branch')");
                });

            migrationBuilder.CreateTable(
                name: "regions",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    region_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regions", x => new { x.institution_id, x.region_id });
                    table.CheckConstraint("ck_regions_status", "status IN ('draft', 'active', 'suspended', 'closed')");
                });

            migrationBuilder.CreateTable(
                name: "branches",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    branch_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    region_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branches", x => new { x.institution_id, x.branch_id });
                    table.CheckConstraint("ck_branches_status", "status IN ('draft', 'active', 'suspended', 'closed')");
                    table.ForeignKey(
                        name: "FK_branches_regions_institution_id_region_id",
                        columns: x => new { x.institution_id, x.region_id },
                        principalSchema: "institution",
                        principalTable: "regions",
                        principalColumns: new[] { "institution_id", "region_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_branches_institution_id_code",
                schema: "institution",
                table: "branches",
                columns: new[] { "institution_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_branches_institution_id_region_id",
                schema: "institution",
                table: "branches",
                columns: new[] { "institution_id", "region_id" });

            migrationBuilder.CreateIndex(
                name: "IX_organisation_status_history_institution_id_resource_type_re~",
                schema: "institution",
                table: "organisation_status_history",
                columns: new[] { "institution_id", "resource_type", "resource_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_regions_institution_id_code",
                schema: "institution",
                table: "regions",
                columns: new[] { "institution_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branches",
                schema: "institution");

            migrationBuilder.DropTable(
                name: "organisation_status_history",
                schema: "institution");

            migrationBuilder.DropTable(
                name: "regions",
                schema: "institution");

            migrationBuilder.DropColumn(
                name: "suspended_by_actor_id",
                schema: "institution",
                table: "institutions");

            migrationBuilder.DropColumn(
                name: "suspension_correlation_id",
                schema: "institution",
                table: "institutions");

            migrationBuilder.DropColumn(
                name: "suspension_reason",
                schema: "institution",
                table: "institutions");
        }
    }
}
