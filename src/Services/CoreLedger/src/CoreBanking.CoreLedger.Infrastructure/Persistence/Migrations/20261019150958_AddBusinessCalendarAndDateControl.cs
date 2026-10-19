using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessCalendarAndDateControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_calendars",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    calendar_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_calendars", x => new { x.institution_id, x.calendar_id });
                });

            migrationBuilder.CreateTable(
                name: "calendar_versions",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    calendar_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: false),
                    initial_business_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    approved_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    approval_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    approval_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_versions", x => new { x.institution_id, x.version_id });
                    table.CheckConstraint("ck_calendar_versions_number", "version_number > 0");
                    table.CheckConstraint("ck_calendar_versions_period", "effective_to >= effective_from");
                    table.CheckConstraint("ck_calendar_versions_status", "status IN ('draft', 'published')");
                    table.ForeignKey(
                        name: "FK_calendar_versions_business_calendars_institution_id_calenda~",
                        columns: x => new { x.institution_id, x.calendar_id },
                        principalSchema: "institution",
                        principalTable: "business_calendars",
                        principalColumns: new[] { "institution_id", "calendar_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "business_date_advance_requests",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expected_current_date = table.Column<DateOnly>(type: "date", nullable: false),
                    new_business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    calendar_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    processing_checkpoint_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requested_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    approved_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    approval_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_date_advance_requests", x => new { x.institution_id, x.request_id });
                    table.CheckConstraint("ck_business_date_requests_forward", "new_business_date > expected_current_date");
                    table.CheckConstraint("ck_business_date_requests_status", "status IN ('pending_approval', 'executed')");
                    table.ForeignKey(
                        name: "FK_business_date_advance_requests_calendar_versions_institutio~",
                        columns: x => new { x.institution_id, x.calendar_version_id },
                        principalSchema: "institution",
                        principalTable: "calendar_versions",
                        principalColumns: new[] { "institution_id", "version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "business_date_controls",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    current_business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    calendar_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    last_advance_request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_date_controls", x => x.institution_id);
                    table.ForeignKey(
                        name: "FK_business_date_controls_calendar_versions_institution_id_cal~",
                        columns: x => new { x.institution_id, x.calendar_version_id },
                        principalSchema: "institution",
                        principalTable: "calendar_versions",
                        principalColumns: new[] { "institution_id", "version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendar_days",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    calendar_date = table.Column<DateOnly>(type: "date", nullable: false),
                    classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_processing_day = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_days", x => new { x.institution_id, x.version_id, x.calendar_date });
                    table.CheckConstraint("ck_calendar_days_classification", "classification IN ('working_day', 'holiday', 'non_working_day')");
                    table.ForeignKey(
                        name: "FK_calendar_days_calendar_versions_institution_id_version_id",
                        columns: x => new { x.institution_id, x.version_id },
                        principalSchema: "institution",
                        principalTable: "calendar_versions",
                        principalColumns: new[] { "institution_id", "version_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_date_history",
                schema: "institution",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    history_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    previous_business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    skipped_non_processing_days = table.Column<int>(type: "integer", nullable: false),
                    calendar_version_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    processing_checkpoint_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    requested_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    approved_by_actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    approval_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    advanced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_date_history", x => new { x.institution_id, x.history_id });
                    table.CheckConstraint("ck_business_date_history_forward", "business_date > previous_business_date");
                    table.CheckConstraint("ck_business_date_history_skipped", "skipped_non_processing_days >= 0");
                    table.ForeignKey(
                        name: "FK_business_date_history_business_date_advance_requests_instit~",
                        columns: x => new { x.institution_id, x.request_id },
                        principalSchema: "institution",
                        principalTable: "business_date_advance_requests",
                        principalColumns: new[] { "institution_id", "request_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_business_date_history_calendar_versions_institution_id_cale~",
                        columns: x => new { x.institution_id, x.calendar_version_id },
                        principalSchema: "institution",
                        principalTable: "calendar_versions",
                        principalColumns: new[] { "institution_id", "version_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_date_advance_requests_institution_id_calendar_vers~",
                schema: "institution",
                table: "business_date_advance_requests",
                columns: new[] { "institution_id", "calendar_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_business_date_advance_requests_institution_id_status_reques~",
                schema: "institution",
                table: "business_date_advance_requests",
                columns: new[] { "institution_id", "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "IX_business_date_controls_institution_id_calendar_version_id",
                schema: "institution",
                table: "business_date_controls",
                columns: new[] { "institution_id", "calendar_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_business_date_history_institution_id_business_date",
                schema: "institution",
                table: "business_date_history",
                columns: new[] { "institution_id", "business_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_date_history_institution_id_calendar_version_id",
                schema: "institution",
                table: "business_date_history",
                columns: new[] { "institution_id", "calendar_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_business_date_history_institution_id_request_id",
                schema: "institution",
                table: "business_date_history",
                columns: new[] { "institution_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_calendar_versions_institution_id_calendar_id_effective_from~",
                schema: "institution",
                table: "calendar_versions",
                columns: new[] { "institution_id", "calendar_id", "effective_from", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_versions_institution_id_calendar_id_version_number",
                schema: "institution",
                table: "calendar_versions",
                columns: new[] { "institution_id", "calendar_id", "version_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_date_controls",
                schema: "institution");

            migrationBuilder.DropTable(
                name: "business_date_history",
                schema: "institution");

            migrationBuilder.DropTable(
                name: "calendar_days",
                schema: "institution");

            migrationBuilder.DropTable(
                name: "business_date_advance_requests",
                schema: "institution");

            migrationBuilder.DropTable(
                name: "calendar_versions",
                schema: "institution");

            migrationBuilder.DropTable(
                name: "business_calendars",
                schema: "institution");
        }
    }
}
