using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.CustomerOnboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKycVerificationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider_route_id",
                schema: "kyc",
                table: "verification_attempts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "evidence_hash",
                schema: "kyc",
                table: "identity_verifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "evidence_reference",
                schema: "kyc",
                table: "identity_verifications",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_identity_verifications_evidence_hash",
                schema: "kyc",
                table: "identity_verifications",
                sql: "evidence_hash IS NULL OR length(evidence_hash) = 64");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_identity_verifications_evidence_hash",
                schema: "kyc",
                table: "identity_verifications");

            migrationBuilder.DropColumn(
                name: "provider_route_id",
                schema: "kyc",
                table: "verification_attempts");

            migrationBuilder.DropColumn(
                name: "evidence_hash",
                schema: "kyc",
                table: "identity_verifications");

            migrationBuilder.DropColumn(
                name: "evidence_reference",
                schema: "kyc",
                table: "identity_verifications");
        }
    }
}
