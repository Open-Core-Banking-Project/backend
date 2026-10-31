using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Completes the session lifecycle schema and creates the security lock table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>identity.sessions</c> was created without the lifecycle columns that ADR-0008 and the
    /// authentication services already rely on. They are added nullable, backfilled from evidence
    /// the row already carries, validated, and only then tightened to <c>NOT NULL</c>. Each step is
    /// separate so a data problem fails with a diagnostic instead of an opaque constraint error.
    /// </para>
    /// <para>
    /// A legacy row never had a provider session binding, so <c>provider_session_fingerprint</c> is
    /// backfilled with the deterministic marker <c>legacy:</c> followed by 32 hexadecimal characters
    /// derived from the institution and session identifiers. A real fingerprint is the 64-character
    /// lowercase hexadecimal SHA-256 digest produced by
    /// <c>IdentityLifecycleService.FingerprintProviderSubject</c>, so the marker can never equal a
    /// provider fingerprint and can never match a provider session. The marker is unique per row,
    /// which keeps the tenant-unique index satisfiable, and it stays inside the 64-character column.
    /// </para>
    /// <para>
    /// An unbound legacy session cannot prove continuity with the provider, so the existing refresh
    /// path refuses it: the fingerprint comparison fails and the session is marked
    /// <c>Compromised</c> and revoked. That is deliberate fail-closed behaviour, not an accident of
    /// the marker. Legacy sessions are short-lived and expire; an operator who must preserve access
    /// re-authenticates the identity instead of editing the row.
    /// </para>
    /// <para>
    /// The Down migration drops the added columns, so backfilled lifecycle state is not restored.
    /// The synthetic fingerprint and derived status cannot be reversed to the absent columns that
    /// preceded them; only the schema is reverted.
    /// </para>
    /// </remarks>
    public partial class AddSessionLifecycleAndSecurityLocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "concurrency_version",
                schema: "identity",
                table: "sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_activity_at",
                schema: "identity",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "provider_revocation_requested_at",
                schema: "identity",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_session_fingerprint",
                schema: "identity",
                table: "sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "revocation_event_id",
                schema: "identity",
                table: "sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "identity",
                table: "sessions",
                type: "text",
                nullable: true);

            // Backfill. A revoked row is Revoked; otherwise an elapsed expiry makes it Expired;
            // otherwise it was Active. last_activity_at uses the last moment the platform knows the
            // row was touched, and concurrency_version starts at zero because no optimistic write
            // has been recorded for these rows yet.
            migrationBuilder.Sql(
                """
                UPDATE identity.sessions
                SET
                    status = CASE
                        WHEN revoked_at IS NOT NULL THEN 'Revoked'
                        WHEN expires_at <= now() THEN 'Expired'
                        ELSE 'Active'
                    END,
                    last_activity_at = COALESCE(revoked_at, issued_at),
                    concurrency_version = 0,
                    provider_session_fingerprint = 'legacy:' || substr(
                        encode(sha256(convert_to(institution_id || ':' || session_id, 'UTF8')), 'hex'),
                        1,
                        32)
                WHERE provider_session_fingerprint IS NULL;
                """);

            // Validate before the schema depends on the backfill. A violation names the count so an
            // operator knows which rule failed without reading the whole table.
            migrationBuilder.Sql(
                """
                DO $migration$
                DECLARE
                    missing_values bigint;
                    invalid_status bigint;
                    invalid_fingerprint bigint;
                    duplicate_fingerprints bigint;
                    lifecycle_mismatch bigint;
                BEGIN
                    SELECT count(*) INTO missing_values
                    FROM identity.sessions
                    WHERE status IS NULL
                       OR last_activity_at IS NULL
                       OR concurrency_version IS NULL
                       OR provider_session_fingerprint IS NULL;

                    SELECT count(*) INTO invalid_status
                    FROM identity.sessions
                    WHERE status IS NOT NULL
                      AND status NOT IN ('Active', 'Refreshed', 'Revoked', 'Compromised', 'Expired');

                    SELECT count(*) INTO invalid_fingerprint
                    FROM identity.sessions
                    WHERE provider_session_fingerprint IS NOT NULL
                      AND (provider_session_fingerprint = '' OR length(provider_session_fingerprint) > 64);

                    SELECT count(*) INTO duplicate_fingerprints
                    FROM (
                        SELECT institution_id, provider_session_fingerprint
                        FROM identity.sessions
                        WHERE provider_session_fingerprint IS NOT NULL
                        GROUP BY institution_id, provider_session_fingerprint
                        HAVING count(*) > 1
                    ) duplicates;

                    SELECT count(*) INTO lifecycle_mismatch
                    FROM identity.sessions
                    WHERE (status = 'Revoked' AND revoked_at IS NULL)
                       OR (status = 'Expired' AND expires_at > now())
                       OR (status = 'Active' AND (expires_at <= now() OR revoked_at IS NOT NULL));

                    IF missing_values + invalid_status + invalid_fingerprint + duplicate_fingerprints + lifecycle_mismatch > 0 THEN
                        RAISE EXCEPTION
                            'Session lifecycle backfill failed: missing_values=%, invalid_status=%, invalid_fingerprint=%, duplicate_fingerprints=%, lifecycle_mismatch=%. Correct identity.sessions before applying AddSessionLifecycleAndSecurityLocks.',
                            missing_values, invalid_status, invalid_fingerprint, duplicate_fingerprints, lifecycle_mismatch;
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "concurrency_version",
                schema: "identity",
                table: "sessions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "last_activity_at",
                schema: "identity",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "provider_session_fingerprint",
                schema: "identity",
                table: "sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "identity",
                table: "sessions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sessions_institution_id_provider_session_fingerprint",
                schema: "identity",
                table: "sessions",
                columns: new[] { "institution_id", "provider_session_fingerprint" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_sessions_status",
                schema: "identity",
                table: "sessions",
                sql: "status IN ('Active', 'Refreshed', 'Revoked', 'Compromised', 'Expired')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sessions_concurrency_version",
                schema: "identity",
                table: "sessions",
                sql: "concurrency_version >= 0");

            migrationBuilder.CreateTable(
                name: "locks",
                schema: "security",
                columns: table => new
                {
                    institution_id = table.Column<string>(type: "text", nullable: false),
                    security_lock_id = table.Column<string>(type: "text", nullable: false),
                    identity_id = table.Column<string>(type: "text", nullable: false),
                    scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_locks", x => new { x.institution_id, x.security_lock_id });
                    table.CheckConstraint("ck_locks_expiry_after_creation", "expires_at IS NULL OR expires_at >= created_at");
                    table.CheckConstraint("ck_locks_release_after_creation", "released_at IS NULL OR released_at >= created_at");
                });

            migrationBuilder.CreateIndex(
                name: "IX_locks_institution_id_identity_id_scope",
                schema: "security",
                table: "locks",
                columns: new[] { "institution_id", "identity_id", "scope" });

            // The application principal's table privileges in this schema come from the default
            // privileges installed by 20261017054933_GrantApplicationPrincipalPrivileges, which only
            // apply to tables created after they were declared. The grant is repeated here so an
            // installation whose defaults were never installed still leaves the new table usable,
            // and it stays guarded so a cluster without the principal is not blocked.
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'identityaccess_app') THEN
                        REVOKE ALL ON TABLE security.locks FROM PUBLIC;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE security.locks TO identityaccess_app;
                    END IF;
                END
                $migration$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sessions_concurrency_version",
                schema: "identity",
                table: "sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sessions_status",
                schema: "identity",
                table: "sessions");

            migrationBuilder.DropTable(
                name: "locks",
                schema: "security");

            migrationBuilder.DropIndex(
                name: "IX_sessions_institution_id_provider_session_fingerprint",
                schema: "identity",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "concurrency_version",
                schema: "identity",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "last_activity_at",
                schema: "identity",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "provider_revocation_requested_at",
                schema: "identity",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "provider_session_fingerprint",
                schema: "identity",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "revocation_event_id",
                schema: "identity",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "identity",
                table: "sessions");
        }
    }
}
