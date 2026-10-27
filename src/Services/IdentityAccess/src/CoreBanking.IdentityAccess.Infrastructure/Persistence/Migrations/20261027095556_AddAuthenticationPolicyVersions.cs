using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticationPolicyVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "authentication_policy_versions",
                schema: "security",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    authentication_policy_version_id = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authentication_policy_versions", x => new { x.institution_id, x.authentication_policy_version_id });
                    table.CheckConstraint("ck_authentication_policy_versions_effective_interval", "effective_until IS NULL OR effective_until > effective_from");
                });

            migrationBuilder.CreateIndex(
                name: "IX_authentication_policy_versions_institution_id_channel_statu~",
                schema: "security",
                table: "authentication_policy_versions",
                columns: new[] { "institution_id", "channel", "status", "effective_from" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authentication_policy_versions",
                schema: "security");
        }
    }
}
