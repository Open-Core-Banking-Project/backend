using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionAuditAndProvisioningRepairEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "institution_audit_events",
                schema: "audit",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    audit_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    actor_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    permission = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    before_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    after_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_institution_audit_events", x => new { x.institution_id, x.audit_event_id });
                    table.CheckConstraint("ck_institution_audit_outcome", "outcome IN ('succeeded', 'rejected', 'failed')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_institution_audit_events_institution_id_correlation_id",
                schema: "audit",
                table: "institution_audit_events",
                columns: new[] { "institution_id", "correlation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_institution_audit_events_institution_id_occurred_at",
                schema: "audit",
                table: "institution_audit_events",
                columns: new[] { "institution_id", "occurred_at" });

            migrationBuilder.Sql(
                "GRANT USAGE ON SCHEMA audit TO coreledger_app; "
                + "GRANT SELECT, INSERT ON TABLE audit.institution_audit_events TO coreledger_app; "
                + "REVOKE UPDATE, DELETE, TRUNCATE ON TABLE audit.institution_audit_events FROM coreledger_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "REVOKE ALL ON TABLE audit.institution_audit_events FROM coreledger_app;");

            migrationBuilder.DropTable(
                name: "institution_audit_events",
                schema: "audit");
        }
    }
}
