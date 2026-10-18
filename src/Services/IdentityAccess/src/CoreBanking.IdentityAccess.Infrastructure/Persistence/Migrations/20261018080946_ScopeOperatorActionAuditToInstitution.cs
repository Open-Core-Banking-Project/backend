using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeOperatorActionAuditToInstitution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_operator_actions",
                schema: "audit",
                table: "operator_actions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_operator_actions",
                schema: "audit",
                table: "operator_actions",
                columns: new[] { "institution_id", "audit_event_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_operator_actions",
                schema: "audit",
                table: "operator_actions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_operator_actions",
                schema: "audit",
                table: "operator_actions",
                column: "audit_event_id");
        }
    }
}
