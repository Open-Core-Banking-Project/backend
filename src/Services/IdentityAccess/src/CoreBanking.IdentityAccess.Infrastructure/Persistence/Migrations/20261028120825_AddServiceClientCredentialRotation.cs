using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceClientCredentialRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "credential_expires_at",
                schema: "identity",
                table: "service_clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "credential_reference",
                schema: "identity",
                table: "service_clients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "previous_credential_reference",
                schema: "identity",
                table: "service_clients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "previous_valid_until",
                schema: "identity",
                table: "service_clients",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "credential_expires_at",
                schema: "identity",
                table: "service_clients");

            migrationBuilder.DropColumn(
                name: "credential_reference",
                schema: "identity",
                table: "service_clients");

            migrationBuilder.DropColumn(
                name: "previous_credential_reference",
                schema: "identity",
                table: "service_clients");

            migrationBuilder.DropColumn(
                name: "previous_valid_until",
                schema: "identity",
                table: "service_clients");
        }
    }
}
