using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFoundationRecordTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "foundation_records",
                schema: "platform",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    foundation_record_id = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_foundation_records", x => new { x.institution_id, x.foundation_record_id });
                });

            migrationBuilder.Sql(
                "GRANT USAGE ON SCHEMA platform TO identityaccess_app;");

            migrationBuilder.Sql(
                "GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA platform TO identityaccess_app;");

            migrationBuilder.Sql(
                "ALTER DEFAULT PRIVILEGES FOR ROLE identityaccess_migrator IN SCHEMA platform "
                + "GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO identityaccess_app;");

            migrationBuilder.Sql(
                "REVOKE ALL ON SCHEMA platform FROM PUBLIC;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER DEFAULT PRIVILEGES FOR ROLE identityaccess_migrator IN SCHEMA platform "
                + "REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM identityaccess_app;");

            migrationBuilder.Sql(
                "REVOKE ALL ON SCHEMA platform FROM identityaccess_app;");
            migrationBuilder.DropTable(
                name: "foundation_records",
                schema: "platform");
        }
    }
}
