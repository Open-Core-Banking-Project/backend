using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDuplicateCandidateSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "customer");

            migrationBuilder.CreateTable(
                name: "duplicate_candidate_sets",
                schema: "customer",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    set_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    application_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    application_revision = table.Column<int>(type: "integer", nullable: false),
                    rule_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_duplicate_candidate_sets", x => new { x.institution_id, x.set_id });
                    table.CheckConstraint("ck_duplicate_candidate_sets_decided", "(status <> 'decided' AND decision IS NULL) OR (status = 'decided' AND decision IS NOT NULL AND decided_at IS NOT NULL AND decided_by IS NOT NULL AND decision_reason IS NOT NULL)");
                    table.CheckConstraint("ck_duplicate_candidate_sets_decision", "decision IS NULL OR decision IN ('same_person', 'distinct', 'insufficient_evidence')");
                    table.CheckConstraint("ck_duplicate_candidate_sets_revision", "application_revision >= 1");
                    table.CheckConstraint("ck_duplicate_candidate_sets_rule_version", "char_length(rule_version) > 0");
                    table.CheckConstraint("ck_duplicate_candidate_sets_status", "status IN ('open', 'decided', 'superseded')");
                    table.CheckConstraint("ck_duplicate_candidate_sets_version", "version > 0");
                    table.ForeignKey(
                        name: "FK_duplicate_candidate_sets_applications_institution_id_applic~",
                        columns: x => new { x.institution_id, x.application_id },
                        principalSchema: "onboarding",
                        principalTable: "applications",
                        principalColumns: new[] { "institution_id", "application_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "duplicate_candidates",
                schema: "customer",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    set_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    candidate_party_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    band = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    factors = table.Column<string>(type: "jsonb", nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_duplicate_candidates", x => new { x.institution_id, x.set_id, x.candidate_party_id });
                    table.CheckConstraint("ck_duplicate_candidates_band", "band IN ('possible', 'probable', 'confirmed')");
                    table.CheckConstraint("ck_duplicate_candidates_factors", "jsonb_array_length(factors) > 0");
                    table.ForeignKey(
                        name: "FK_duplicate_candidates_duplicate_candidate_sets_institution_i~",
                        columns: x => new { x.institution_id, x.set_id },
                        principalSchema: "customer",
                        principalTable: "duplicate_candidate_sets",
                        principalColumns: new[] { "institution_id", "set_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_candidate_sets_institution_status_created",
                schema: "customer",
                table: "duplicate_candidate_sets",
                columns: new[] { "institution_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_duplicate_candidate_sets_live",
                schema: "customer",
                table: "duplicate_candidate_sets",
                columns: new[] { "institution_id", "application_id", "application_revision" },
                unique: true,
                filter: "status <> 'superseded'");

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_candidates_institution_party",
                schema: "customer",
                table: "duplicate_candidates",
                columns: new[] { "institution_id", "candidate_party_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "duplicate_candidates",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "duplicate_candidate_sets",
                schema: "customer");
        }
    }
}
