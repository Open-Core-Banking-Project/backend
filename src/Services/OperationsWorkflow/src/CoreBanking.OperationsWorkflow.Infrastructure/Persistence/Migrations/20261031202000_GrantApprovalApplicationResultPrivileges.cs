using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using CoreBanking.OperationsWorkflow.Infrastructure.Persistence;

#nullable disable

namespace CoreBanking.OperationsWorkflow.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Grants the application principal the least privilege it needs on the new <c>approval</c> schema that holds
    /// source-service acknowledgements. The earlier privileges migration predates this schema, so the grants are
    /// repeated here for the new schema only. Decision: ADR-0004.
    /// </summary>
    [DbContext(typeof(OperationsWorkflowDbContext))]
    [Migration("20261031202000_GrantApprovalApplicationResultPrivileges")]
    public partial class GrantApprovalApplicationResultPrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'operationsworkflow_app') THEN
                        RAISE NOTICE 'The operationsworkflow_app role does not exist; approval-schema privileges were not installed.';
                        RETURN;
                    END IF;

                    REVOKE ALL ON SCHEMA approval FROM PUBLIC;
                    GRANT USAGE ON SCHEMA approval TO operationsworkflow_app;

                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA approval TO operationsworkflow_app;
                    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA approval TO operationsworkflow_app;

                    ALTER DEFAULT PRIVILEGES FOR ROLE operationsworkflow_migrator IN SCHEMA approval
                        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO operationsworkflow_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE operationsworkflow_migrator IN SCHEMA approval
                        GRANT USAGE, SELECT ON SEQUENCES TO operationsworkflow_app;
                END
                $migration$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'operationsworkflow_app') THEN
                        RETURN;
                    END IF;

                    ALTER DEFAULT PRIVILEGES FOR ROLE operationsworkflow_migrator IN SCHEMA approval
                        REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM operationsworkflow_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE operationsworkflow_migrator IN SCHEMA approval
                        REVOKE USAGE, SELECT ON SEQUENCES FROM operationsworkflow_app;

                    REVOKE ALL ON ALL SEQUENCES IN SCHEMA approval FROM operationsworkflow_app;
                    REVOKE ALL ON ALL TABLES IN SCHEMA approval FROM operationsworkflow_app;
                    REVOKE ALL ON SCHEMA approval FROM operationsworkflow_app;
                END
                $migration$;
                """);
        }
    }
}
