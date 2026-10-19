using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionActivationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "activated_by_actor_id",
                schema: "institution",
                table: "institutions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "activation_correlation_id",
                schema: "institution",
                table: "institutions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_actor_id",
                schema: "institution",
                table: "institutions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "institution",
                table: "institutions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "activated_by_actor_id",
                schema: "institution",
                table: "institutions");

            migrationBuilder.DropColumn(
                name: "activation_correlation_id",
                schema: "institution",
                table: "institutions");

            migrationBuilder.DropColumn(
                name: "created_by_actor_id",
                schema: "institution",
                table: "institutions");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "institution",
                table: "institutions");
        }
    }
}
