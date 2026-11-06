using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleConcurrencyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "concurrency_version",
                schema: "access",
                table: "roles",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.Sql("ALTER TABLE access.roles ALTER COLUMN concurrency_version DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "concurrency_version",
                schema: "access",
                table: "roles");
        }
    }
}
