using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementPacksAndDocumentReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "kyc");

            migrationBuilder.AddColumn<string>(
                name: "requirement_snapshot_id",
                schema: "onboarding",
                table: "applications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "requirement_snapshot_id",
                schema: "onboarding",
                table: "application_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "document_references",
                schema: "kyc",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    document_reference_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    application_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requirement_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    content_length = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    storage_reference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    scan_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    registered_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_references", x => new { x.institution_id, x.document_reference_id });
                    table.CheckConstraint("ck_document_references_content_hash", "char_length(content_hash) = 64");
                    table.CheckConstraint("ck_document_references_content_length", "content_length > 0");
                    table.CheckConstraint("ck_document_references_scan_status", "scan_status IN ('pending_scan', 'clean', 'rejected')");
                });

            migrationBuilder.CreateTable(
                name: "requirement_packs",
                schema: "onboarding",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    pack_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requirements = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    published_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_packs", x => new { x.institution_id, x.pack_code, x.version });
                    table.CheckConstraint("ck_requirement_packs_content_hash", "char_length(content_hash) = 64");
                    table.CheckConstraint("ck_requirement_packs_status", "status IN ('effective', 'superseded')");
                    table.CheckConstraint("ck_requirement_packs_superseded", "(status = 'superseded') = (superseded_at IS NOT NULL)");
                    table.CheckConstraint("ck_requirement_packs_version", "version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "requirement_snapshots",
                schema: "onboarding",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    requirement_snapshot_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    application_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    application_revision = table.Column<int>(type: "integer", nullable: false),
                    pack_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pack_version = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requirements = table.Column<string>(type: "jsonb", nullable: false),
                    captured_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_snapshots", x => new { x.institution_id, x.requirement_snapshot_id });
                    table.CheckConstraint("ck_requirement_snapshots_content_hash", "char_length(content_hash) = 64");
                    table.CheckConstraint("ck_requirement_snapshots_pack_version", "pack_version >= 1");
                    table.CheckConstraint("ck_requirement_snapshots_revision", "application_revision >= 1");
                    table.ForeignKey(
                        name: "FK_requirement_snapshots_applications_institution_id_applicati~",
                        columns: x => new { x.institution_id, x.application_id },
                        principalSchema: "onboarding",
                        principalTable: "applications",
                        principalColumns: new[] { "institution_id", "application_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_references_institution_application_status",
                schema: "kyc",
                table: "document_references",
                columns: new[] { "institution_id", "application_id", "scan_status" });

            migrationBuilder.CreateIndex(
                name: "ux_requirement_packs_effective",
                schema: "onboarding",
                table: "requirement_packs",
                column: "institution_id",
                unique: true,
                filter: "status = 'effective'");

            migrationBuilder.CreateIndex(
                name: "ux_requirement_snapshots_application_revision",
                schema: "onboarding",
                table: "requirement_snapshots",
                columns: new[] { "institution_id", "application_id", "application_revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_references",
                schema: "kyc");

            migrationBuilder.DropTable(
                name: "requirement_packs",
                schema: "onboarding");

            migrationBuilder.DropTable(
                name: "requirement_snapshots",
                schema: "onboarding");

            migrationBuilder.DropColumn(
                name: "requirement_snapshot_id",
                schema: "onboarding",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "requirement_snapshot_id",
                schema: "onboarding",
                table: "application_revisions");
        }
    }
}
