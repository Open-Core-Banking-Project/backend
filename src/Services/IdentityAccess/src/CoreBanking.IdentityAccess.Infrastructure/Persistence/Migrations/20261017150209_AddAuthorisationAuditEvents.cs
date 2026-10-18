using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorisationAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "authorisation_events",
                schema: "audit",
                columns: table => new
                {
                    audit_event_id = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<string>(type: "text", nullable: true),
                    actor_type = table.Column<string>(type: "text", nullable: true),
                    institution_id = table.Column<string>(type: "text", nullable: true),
                    permission = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    decision = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    entitlement_version = table.Column<long>(type: "bigint", nullable: true),
                    session_id = table.Column<string>(type: "text", nullable: true),
                    device_id = table.Column<string>(type: "text", nullable: true),
                    policy_version = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authorisation_events", x => x.audit_event_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_authorisation_events_institution_id_occurred_at",
                schema: "audit",
                table: "authorisation_events",
                columns: new[] { "institution_id", "occurred_at" });

            migrationBuilder.Sql(
                "REVOKE ALL ON SCHEMA audit FROM PUBLIC;"
                + "GRANT USAGE ON SCHEMA audit TO identityaccess_app;"
                + "GRANT SELECT, INSERT ON TABLE audit.authorisation_events TO identityaccess_app;"
                + "REVOKE UPDATE, DELETE, TRUNCATE ON TABLE audit.authorisation_events FROM identityaccess_app;"
                + "ALTER DEFAULT PRIVILEGES FOR ROLE identityaccess_migrator IN SCHEMA audit "
                + "GRANT SELECT, INSERT ON TABLES TO identityaccess_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER DEFAULT PRIVILEGES FOR ROLE identityaccess_migrator IN SCHEMA audit "
                + "REVOKE SELECT, INSERT ON TABLES FROM identityaccess_app;"
                + "REVOKE ALL ON SCHEMA audit FROM identityaccess_app;");

            migrationBuilder.DropTable(
                name: "authorisation_events",
                schema: "audit");
        }
    }
}
