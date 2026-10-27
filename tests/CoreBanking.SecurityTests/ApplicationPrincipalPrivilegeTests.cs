using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Xunit;

namespace CoreBanking.SecurityTests;

/// <summary>
/// Effective privileges of every application principal, checked against the databases it really
/// connects to. The assertions use <c>has_table_privilege</c> and <c>has_schema_privilege</c>, which
/// account for role membership, object ownership and PUBLIC grants, so a passing test means the
/// principal can actually perform the operation and not merely that a GRANT statement ran.
/// The suite needs every database migrated and is run by <c>scripts/db-verify.ps1</c>.
/// Decision: docs/adr/ADR-0004-database-per-service-and-tenant-isolation.md.
/// </summary>
[Trait("Category", "Database")]
public sealed class ApplicationPrincipalPrivilegeTests
{
    public static IEnumerable<object[]> Services() =>
        ServiceCatalogue.All.Select(service => new object[] { service.Name });

    [Theory]
    [MemberData(nameof(Services))]
    public async Task The_principal_can_use_every_schema_but_cannot_create_objects(string serviceName)
    {
        ServiceDescriptor service = ServiceCatalogue.Get(serviceName);
        await using NpgsqlConnection connection = await service.OpenAsync(CancellationToken.None);

        SchemaPrivileges[] privileges = await ReadSchemaPrivilegesAsync(connection, service.AllSchemas);

        Assert.Equal(service.AllSchemas.Length, privileges.Length);
        foreach (SchemaPrivileges schema in privileges)
        {
            Assert.True(schema.Usage, $"{service.ApplicationRole} must have USAGE on {schema.Name}.");
            Assert.False(schema.Create, $"{service.ApplicationRole} must not have CREATE on {schema.Name}.");
        }
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task The_principal_has_full_data_privileges_on_its_own_domain_tables(string serviceName)
    {
        ServiceDescriptor service = ServiceCatalogue.Get(serviceName);
        await using NpgsqlConnection connection = await service.OpenAsync(CancellationToken.None);

        int inspected = 0;
        foreach (string schema in service.MutableSchemas)
        {
            TablePrivileges[] tables = await ReadTablePrivilegesAsync(connection, schema);
            foreach (TablePrivileges table in tables)
            {
                inspected++;
                Assert.True(table.Select, $"{service.ApplicationRole} must read {schema}.{table.Name}.");
                Assert.True(table.Insert, $"{service.ApplicationRole} must insert into {schema}.{table.Name}.");
                Assert.True(table.Update, $"{service.ApplicationRole} must update {schema}.{table.Name}.");
                Assert.True(table.Delete, $"{service.ApplicationRole} must delete from {schema}.{table.Name}.");
                Assert.False(table.Truncate, $"{service.ApplicationRole} must not truncate {schema}.{table.Name}.");
            }
        }

        Assert.True(inspected > 0, $"{service.Name} has no tables in its mutable schemas.");
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task The_principal_cannot_rewrite_audit_evidence(string serviceName)
    {
        ServiceDescriptor service = ServiceCatalogue.Get(serviceName);
        if (service.AuditSchemas.Length == 0)
        {
            return;
        }

        await using NpgsqlConnection connection = await service.OpenAsync(CancellationToken.None);

        int inspected = 0;
        foreach (string schema in service.AuditSchemas)
        {
            TablePrivileges[] tables = await ReadTablePrivilegesAsync(connection, schema);
            foreach (TablePrivileges table in tables)
            {
                inspected++;
                Assert.True(table.Select, $"{service.ApplicationRole} must read {schema}.{table.Name}.");
                Assert.True(table.Insert, $"{service.ApplicationRole} must append to {schema}.{table.Name}.");
                Assert.False(table.Update, $"{service.ApplicationRole} must not update {schema}.{table.Name}.");
                Assert.False(table.Delete, $"{service.ApplicationRole} must not delete from {schema}.{table.Name}.");
                Assert.False(table.Truncate, $"{service.ApplicationRole} must not truncate {schema}.{table.Name}.");
            }
        }

        Assert.True(inspected > 0, $"{service.Name} has no tables in its audit schema.");
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task The_principal_holds_no_ownership_membership_or_administrative_attribute(string serviceName)
    {
        ServiceDescriptor service = ServiceCatalogue.Get(serviceName);
        await using NpgsqlConnection connection = await service.OpenAsync(CancellationToken.None);

        await using NpgsqlCommand command = new(
            """
            SELECT r.rolsuper,
                   r.rolcreatedb,
                   r.rolcreaterole,
                   r.rolbypassrls,
                   pg_has_role(current_user, @migrator, 'member'),
                   (SELECT count(*) FROM pg_namespace WHERE nspowner = r.oid),
                   (SELECT count(*) FROM pg_class WHERE relowner = r.oid)
            FROM pg_roles r
            WHERE r.rolname = current_user
            """,
            connection);
        command.Parameters.AddWithValue("migrator", service.MigrationRole);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync(CancellationToken.None));

        Assert.False(reader.GetBoolean(0), "The application principal must not be a superuser.");
        Assert.False(reader.GetBoolean(1), "The application principal must not create databases.");
        Assert.False(reader.GetBoolean(2), "The application principal must not create roles.");
        Assert.False(reader.GetBoolean(3), "The application principal must not bypass row-level security.");
        Assert.False(reader.GetBoolean(4), "The application principal must not be a member of the migration role.");
        Assert.Equal(0L, reader.GetInt64(5));
        Assert.Equal(0L, reader.GetInt64(6));
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Only_its_own_database_grants_connect_to_the_principal(string serviceName)
    {
        ServiceDescriptor service = ServiceCatalogue.Get(serviceName);
        await using NpgsqlConnection connection = await service.OpenAsync(CancellationToken.None);

        await using (NpgsqlCommand command = new(
            "SELECT datname, has_database_privilege(current_user, datname, 'CONNECT') FROM pg_database WHERE datname = ANY(@databases) ORDER BY datname",
            connection))
        {
            command.Parameters.AddWithValue("databases", ServiceCatalogue.All.Select(entry => entry.Database).ToArray());

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
            Dictionary<string, bool> connect = [];
            while (await reader.ReadAsync(CancellationToken.None))
            {
                connect[reader.GetString(0)] = reader.GetBoolean(1);
            }

            Assert.Equal(ServiceCatalogue.All.Length, connect.Count);
            foreach (KeyValuePair<string, bool> entry in connect)
            {
                Assert.Equal(entry.Key == service.Database, entry.Value);
            }
        }

        ServiceDescriptor foreign = ServiceCatalogue.All.First(entry => entry.Database != service.Database);
        NpgsqlConnectionStringBuilder builder = new(service.ConnectionString) { Database = foreign.Database };

        await using NpgsqlConnection denied = new(builder.ConnectionString);
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => denied.OpenAsync(CancellationToken.None));
        Assert.Equal("42501", exception.SqlState);
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task The_principal_cannot_create_a_table_in_a_schema_it_uses(string serviceName)
    {
        ServiceDescriptor service = ServiceCatalogue.Get(serviceName);
        await using NpgsqlConnection connection = await service.OpenAsync(CancellationToken.None);

        string schema = service.MutableSchemas[0];
        await using NpgsqlCommand command = new(
            $"CREATE TABLE {schema}.privilege_probe_{Guid.NewGuid():N} (id integer)",
            connection);

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(CancellationToken.None));
        Assert.Equal("42501", exception.SqlState);
    }

    private static async Task<SchemaPrivileges[]> ReadSchemaPrivilegesAsync(NpgsqlConnection connection, string[] schemas)
    {
        await using NpgsqlCommand command = new(
            """
            SELECT nspname,
                   has_schema_privilege(current_user, nspname, 'USAGE'),
                   has_schema_privilege(current_user, nspname, 'CREATE')
            FROM pg_namespace
            WHERE nspname = ANY(@schemas)
            ORDER BY nspname
            """,
            connection);
        command.Parameters.AddWithValue("schemas", schemas);

        List<SchemaPrivileges> privileges = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None))
        {
            privileges.Add(new SchemaPrivileges(reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2)));
        }

        return [.. privileges];
    }

    private static async Task<TablePrivileges[]> ReadTablePrivilegesAsync(NpgsqlConnection connection, string schema)
    {
        await using NpgsqlCommand command = new(
            """
            SELECT c.relname,
                   has_table_privilege(current_user, c.oid, 'SELECT'),
                   has_table_privilege(current_user, c.oid, 'INSERT'),
                   has_table_privilege(current_user, c.oid, 'UPDATE'),
                   has_table_privilege(current_user, c.oid, 'DELETE'),
                   has_table_privilege(current_user, c.oid, 'TRUNCATE')
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relkind = 'r'
            ORDER BY c.relname
            """,
            connection);
        command.Parameters.AddWithValue("schema", schema);

        List<TablePrivileges> privileges = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None))
        {
            privileges.Add(new TablePrivileges(
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5)));
        }

        return [.. privileges];
    }

    private sealed record SchemaPrivileges(string Name, bool Usage, bool Create);

    private sealed record TablePrivileges(string Name, bool Select, bool Insert, bool Update, bool Delete, bool Truncate);
}
