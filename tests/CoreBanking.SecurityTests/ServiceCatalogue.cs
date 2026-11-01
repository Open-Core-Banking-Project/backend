using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace CoreBanking.SecurityTests;

/// <summary>
/// The nine service databases, their two principals and the schemas each service owns. The role,
/// database and schema names are the ones <c>deploy/compose/postgres/init/01-create-databases.sh</c>
/// creates and the migrations in each service grant against.
/// </summary>
internal sealed class ServiceDescriptor
{
    public ServiceDescriptor(string name, string database, string applicationRole, string migrationRole, string[] mutableSchemas, string[] auditSchemas)
    {
        Name = name;
        Database = database;
        ApplicationRole = applicationRole;
        MigrationRole = migrationRole;
        MutableSchemas = mutableSchemas;
        AuditSchemas = auditSchemas;
        AllSchemas = [.. mutableSchemas, .. auditSchemas];
    }

    public string Name { get; }

    public string Database { get; }

    public string ApplicationRole { get; }

    public string MigrationRole { get; }

    public string[] MutableSchemas { get; }

    public string[] AuditSchemas { get; }

    public string[] AllSchemas { get; }

    public string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__" + Name)
        ?? throw new InvalidOperationException(
            $"Set ConnectionStrings__{Name} to the {ApplicationRole} connection string, or run scripts/db-verify.ps1.");

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection connection = new(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}

internal static class ServiceCatalogue
{
    private static readonly ServiceDescriptor[] Descriptors =
    [
        new("CoreLedger", "core_ledger_db", "coreledger_app", "coreledger_migrator",
            ["institution", "product", "integration"], ["audit"]),
        new("CustomerOnboarding", "customer_onboarding_db", "customeronboarding_app", "customeronboarding_migrator",
            ["party", "onboarding", "customer", "kyc", "privacy", "platform", "integration"], ["audit"]),
        new("IdentityAccess", "identity_access_db", "identityaccess_app", "identityaccess_migrator",
            ["identity", "access", "security", "platform", "integration"], ["audit"]),
        new("PaymentsVtu", "payments_vtu_db", "paymentsvtu_app", "paymentsvtu_migrator",
            ["platform", "integration"], []),
        new("ChannelsEngagement", "channels_engagement_db", "channelsengagement_app", "channelsengagement_migrator",
            ["platform", "integration"], []),
        new("OperationsWorkflow", "operations_workflow_db", "operationsworkflow_app", "operationsworkflow_migrator",
            ["platform", "workflow", "approval", "integration"], []),
        new("RiskReporting", "risk_reporting_db", "riskreporting_app", "riskreporting_migrator",
            ["platform", "integration"], []),
        new("IntegrationHub", "integration_hub_db", "integrationhub_app", "integrationhub_migrator",
            ["platform", "partner", "provider", "integration"], []),
        new("AuditReconciliation", "audit_reconciliation_db", "auditreconciliation_app", "auditreconciliation_migrator",
            ["platform", "integration"], ["audit"]),
    ];

    public static ServiceDescriptor[] All => Descriptors;

    public static ServiceDescriptor Get(string serviceName) =>
        Descriptors.SingleOrDefault(entry => entry.Name == serviceName)
        ?? throw new ArgumentOutOfRangeException(nameof(serviceName), serviceName, "Unknown service.");
}
