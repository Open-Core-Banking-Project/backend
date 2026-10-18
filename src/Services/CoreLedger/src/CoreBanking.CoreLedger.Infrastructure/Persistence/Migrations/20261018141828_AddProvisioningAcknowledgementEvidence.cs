using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProvisioningAcknowledgementEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "acknowledged_at",
                schema: "institution",
                table: "provisioning_steps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acknowledgement_event_id",
                schema: "institution",
                table: "provisioning_steps",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "acknowledged_at",
                schema: "institution",
                table: "provisioning_steps");

            migrationBuilder.DropColumn(
                name: "acknowledgement_event_id",
                schema: "institution",
                table: "provisioning_steps");
        }
    }
}
