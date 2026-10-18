using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInstitutionProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "institution");

            migrationBuilder.CreateTable(
                name: "institutions",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    base_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provisioning_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provisioning_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_provisioning_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provisioning_plan_version = table.Column<int>(type: "integer", nullable: true),
                    ready_for_activation_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_institutions", x => x.institution_id);
                    table.CheckConstraint("ck_institutions_plan_version", "provisioning_plan_version IS NULL OR provisioning_plan_version > 0");
                    table.CheckConstraint("ck_institutions_status", "status IN ('draft', 'provisioning', 'provisioning_failed', 'pending_activation', 'active', 'suspended')");
                });

            migrationBuilder.CreateTable(
                name: "provisioning_runs",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provisioning_run_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    plan_version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provisioning_runs", x => new { x.institution_id, x.provisioning_run_id });
                    table.CheckConstraint("ck_provisioning_runs_plan_version", "plan_version > 0");
                    table.CheckConstraint("ck_provisioning_runs_status", "status IN ('provisioning', 'provisioning_failed', 'pending_activation', 'completed')");
                    table.ForeignKey(
                        name: "FK_provisioning_runs_institutions_institution_id",
                        column: x => x.institution_id,
                        principalSchema: "institution",
                        principalTable: "institutions",
                        principalColumn: "institution_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "provisioning_steps",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provisioning_run_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    service_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provisioning_steps", x => new { x.institution_id, x.provisioning_run_id, x.service_id });
                    table.CheckConstraint("ck_provisioning_steps_failure_code", "(status = 'failed' AND failure_code IS NOT NULL) OR (status <> 'failed' AND failure_code IS NULL)");
                    table.CheckConstraint("ck_provisioning_steps_status", "status IN ('pending', 'initialised', 'failed')");
                    table.ForeignKey(
                        name: "FK_provisioning_steps_provisioning_runs_institution_id_provisi~",
                        columns: x => new { x.institution_id, x.provisioning_run_id },
                        principalSchema: "institution",
                        principalTable: "provisioning_runs",
                        principalColumns: new[] { "institution_id", "provisioning_run_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_institutions_code",
                schema: "institution",
                table: "institutions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provisioning_runs_institution_id",
                schema: "institution",
                table: "provisioning_runs",
                column: "institution_id",
                unique: true,
                filter: "status IN ('provisioning', 'provisioning_failed', 'pending_activation')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provisioning_steps",
                schema: "institution");

            migrationBuilder.DropTable(
                name: "provisioning_runs",
                schema: "institution");

            migrationBuilder.DropTable(
                name: "institutions",
                schema: "institution");
        }
    }
}
