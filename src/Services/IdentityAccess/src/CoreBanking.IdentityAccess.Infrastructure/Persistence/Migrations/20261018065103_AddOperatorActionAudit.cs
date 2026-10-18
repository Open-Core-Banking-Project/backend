using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorActionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operator_actions",
                schema: "audit",
                columns: table => new
                {
                    audit_event_id = table.Column<string>(type: "text", nullable: false),
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    resource_type = table.Column<string>(type: "text", nullable: false),
                    resource_id = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operator_actions", x => x.audit_event_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_operator_actions_institution_id_occurred_at",
                schema: "audit",
                table: "operator_actions",
                columns: new[] { "institution_id", "occurred_at" });

            migrationBuilder.Sql(
                "GRANT SELECT, INSERT ON TABLE audit.operator_actions TO identityaccess_app;"
                + "REVOKE UPDATE, DELETE, TRUNCATE ON TABLE audit.operator_actions FROM identityaccess_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operator_actions",
                schema: "audit");
        }
    }
}
