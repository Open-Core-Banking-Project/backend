using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionConfigurationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "configuration_sets",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    configuration_set_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    area = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    scope = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_sets", x => new { x.institution_id, x.configuration_set_id });
                });

            migrationBuilder.CreateTable(
                name: "configuration_versions",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    configuration_set_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    schema = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    content_json = table.Column<string>(type: "jsonb", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    rollback_of_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    approved_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    approval_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_versions", x => new { x.institution_id, x.version_id });
                    table.CheckConstraint("ck_configuration_versions_effective_period", "(effective_to IS NULL OR (effective_from IS NOT NULL AND effective_to > effective_from)) AND (status NOT IN ('published', 'superseded') OR effective_from IS NOT NULL)");
                    table.CheckConstraint("ck_configuration_versions_number", "version_number > 0");
                    table.CheckConstraint("ck_configuration_versions_status", "status IN ('draft', 'submitted', 'approved', 'published', 'superseded')");
                    table.ForeignKey(
                        name: "FK_configuration_versions_configuration_sets_institution_id_co~",
                        columns: x => new { x.institution_id, x.configuration_set_id },
                        principalSchema: "institution",
                        principalTable: "configuration_sets",
                        principalColumns: new[] { "institution_id", "configuration_set_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_configuration_versions_configuration_versions_institution_i~",
                        columns: x => new { x.institution_id, x.rollback_of_version_id },
                        principalSchema: "institution",
                        principalTable: "configuration_versions",
                        principalColumns: new[] { "institution_id", "version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "configuration_publications",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    publication_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    configuration_set_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    published_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    approval_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_publications", x => new { x.institution_id, x.publication_id });
                    table.CheckConstraint("ck_configuration_publications_effective_period", "effective_to IS NULL OR effective_to > effective_from");
                    table.ForeignKey(
                        name: "FK_configuration_publications_configuration_sets_institution_i~",
                        columns: x => new { x.institution_id, x.configuration_set_id },
                        principalSchema: "institution",
                        principalTable: "configuration_sets",
                        principalColumns: new[] { "institution_id", "configuration_set_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_configuration_publications_configuration_versions_instituti~",
                        columns: x => new { x.institution_id, x.version_id },
                        principalSchema: "institution",
                        principalTable: "configuration_versions",
                        principalColumns: new[] { "institution_id", "version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "configuration_simulations",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    simulation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    dataset_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    engine = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    output_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    simulated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_simulations", x => new { x.institution_id, x.simulation_id });
                    table.CheckConstraint("ck_configuration_simulations_outcome", "outcome IN ('passed', 'failed')");
                    table.ForeignKey(
                        name: "FK_configuration_simulations_configuration_versions_institutio~",
                        columns: x => new { x.institution_id, x.version_id },
                        principalSchema: "institution",
                        principalTable: "configuration_versions",
                        principalColumns: new[] { "institution_id", "version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_configuration_publications_institution_id_configuration_set~",
                schema: "institution",
                table: "configuration_publications",
                columns: new[] { "institution_id", "configuration_set_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_configuration_publications_institution_id_version_id",
                schema: "institution",
                table: "configuration_publications",
                columns: new[] { "institution_id", "version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_configuration_sets_institution_id_area_scope",
                schema: "institution",
                table: "configuration_sets",
                columns: new[] { "institution_id", "area", "scope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_configuration_simulations_institution_id_version_id_input_h~",
                schema: "institution",
                table: "configuration_simulations",
                columns: new[] { "institution_id", "version_id", "input_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_configuration_versions_institution_id_configuration_set_id_~",
                schema: "institution",
                table: "configuration_versions",
                columns: new[] { "institution_id", "configuration_set_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_configuration_versions_institution_id_configuration_set_id~1",
                schema: "institution",
                table: "configuration_versions",
                columns: new[] { "institution_id", "configuration_set_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_configuration_versions_institution_id_rollback_of_version_id",
                schema: "institution",
                table: "configuration_versions",
                columns: new[] { "institution_id", "rollback_of_version_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuration_publications",
                schema: "institution");

            migrationBuilder.DropTable(
                name: "configuration_simulations",
                schema: "institution");

            migrationBuilder.DropTable(
                name: "configuration_versions",
                schema: "institution");

            migrationBuilder.DropTable(
                name: "configuration_sets",
                schema: "institution");
        }
    }
}
