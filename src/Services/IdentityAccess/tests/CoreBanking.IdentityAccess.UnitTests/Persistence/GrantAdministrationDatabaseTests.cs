using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Infrastructure.Access;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Access;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Persistence;

/// <summary>
/// Grant administration against a real <c>identity_access_db</c>, as the application principal.
/// Excluded from the default run and executed by <c>scripts/db-verify.ps1</c>.
/// Decision: ADR-0009, P1-E02-D10.
/// </summary>
[Trait("Category", "Database")]
public sealed class GrantAdministrationDatabaseTests : IDisposable
{
    private const string ConnectionStringVariableName = "ConnectionStrings__IdentityAccess";
    private const string UpdatePermission = "product.version.update";
    private const string PublishPermission = "product.version.publish";
    private const string RoleId = "role-update";
    private const string ConflictingRoleId = "role-publish";

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("grant-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("grant-other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _admin = ActorId.From("grant-admin-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _target = ActorId.From("grant-target-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.Zero));

    public GrantAdministrationDatabaseTests()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringVariableName} to the application principal connection string, or run scripts/db-verify.ps1.");
        }

        _connectionString = configured;
        Seed();
    }

    [Fact]
    public async Task Assigning_a_role_advances_the_version_and_writes_the_invalidation_event()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        GrantResultV1 result = await CreateService(context).AssignRoleAsync(
            _institution, _admin, _target.Value, RoleId, null, "branch-1", null, null, "correlation-1", CancellationToken.None);

        Assert.Equal(1, result.EntitlementVersion);

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        Assert.Equal(1, (await verification.EntitlementVersions.SingleAsync()).Version);
        OutboxRecordWrapper eventRecord = await SingleEntitlementEventAsync(verification);
        Assert.Equal(_target.Value, eventRecord.BusinessReference);
        Assert.Equal(1, ReadNewVersion(eventRecord.Envelope));
        Assert.Contains(GrantContractV1.RoleAssigned, eventRecord.Envelope, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_conflicting_assignment_is_refused_and_persists_nothing()
    {
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            await CreateService(context).AssignRoleAsync(
                _institution, _admin, _target.Value, RoleId, null, null, null, null, "correlation-2", CancellationToken.None);
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            GrantAdministrationException exception = await Assert.ThrowsAsync<GrantAdministrationException>(() =>
                CreateService(context).AssignRoleAsync(
                    _institution, _admin, _target.Value, ConflictingRoleId, null, null, null, null, "correlation-3", CancellationToken.None));
            Assert.Equal(GrantContractV1.ConflictOfDuty, exception.Code);
        }

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        Assert.Single(verification.IdentityRoles);
        Assert.Equal(1, (await verification.EntitlementVersions.SingleAsync()).Version);
    }

    [Fact]
    public async Task An_expired_temporary_grant_advances_the_version_and_keeps_its_history()
    {
        string grantId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            GrantResultV1 result = await CreateService(context).GrantTemporaryPermissionAsync(
                _institution,
                _admin,
                _target.Value,
                UpdatePermission,
                null,
                null,
                _time.GetUtcNow().AddMinutes(30),
                "Temporary access for a migration review",
                "correlation-4",
                CancellationToken.None);
            grantId = result.GrantId;
            Assert.Equal(1, result.EntitlementVersion);
        }

        _time.Advance(TimeSpan.FromMinutes(31));

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            int expired = await CreateService(context).ExpireDueTemporaryGrantsAsync(_time.GetUtcNow(), "correlation-5", CancellationToken.None);
            Assert.Equal(1, expired);
        }

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        TemporaryGrantRecord grant = await verification.TemporaryGrants.SingleAsync();
        Assert.Equal(grantId, grant.TemporaryGrantId);
        Assert.NotNull(grant.ExpiredAt);
        Assert.Equal(2, (await verification.EntitlementVersions.SingleAsync()).Version);

        OutboxRecordWrapper[] events = (await verification.OutboxMessages.ToArrayAsync())
            .Select(record => new OutboxRecordWrapper(record.EventType, record.Envelope, record.BusinessReference))
            .ToArray();
        Assert.Contains(events, record => record.Envelope.Contains(GrantContractV1.TemporaryGrantExpired, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Revoking_a_role_advances_the_version()
    {
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            await CreateService(context).AssignRoleAsync(
                _institution, _admin, _target.Value, RoleId, null, null, null, null, "correlation-6", CancellationToken.None);
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            long version = await CreateService(context).RevokeRoleAsync(
                _institution, _admin, _target.Value, RoleId, "correlation-7", CancellationToken.None);
            Assert.Equal(2, version);
        }

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        Assert.Empty(verification.IdentityRoles);
        Assert.Equal(2, (await verification.EntitlementVersions.SingleAsync()).Version);
    }

    [Fact]
    public async Task A_role_from_another_institution_is_not_found()
    {
        SeedRole(_otherInstitution, "role-foreign", "Foreign role", PublishPermission);

        await using IdentityAccessDbContext context = CreateContext(_institution);
        GrantAdministrationException exception = await Assert.ThrowsAsync<GrantAdministrationException>(() =>
            CreateService(context).AssignRoleAsync(
                _institution, _admin, _target.Value, "role-foreign", null, null, null, null, "correlation-8", CancellationToken.None));

        Assert.Equal(GrantContractV1.RoleNotFound, exception.Code);
    }

    [Fact]
    public async Task The_scheduled_sweep_closes_lapsed_grants_without_a_tenant_context()
    {
        string grantId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            GrantResultV1 result = await CreateService(context).GrantTemporaryPermissionAsync(
                _institution,
                _admin,
                _target.Value,
                UpdatePermission,
                null,
                null,
                _time.GetUtcNow().AddMinutes(15),
                "Temporary access for a scheduled sweep test",
                "correlation-9",
                CancellationToken.None);
            grantId = result.GrantId;
        }

        _time.Advance(TimeSpan.FromMinutes(16));

        // The worker has no request context: the sweep must still close the grant and advance the
        // version using the row's own institution.
        await using (IdentityAccessDbContext context = CreateContext(null))
        {
            int expired = await CreateService(context).ExpireDueTemporaryGrantsAsync(
                _time.GetUtcNow(), "identity.temporary_grant.expiry_sweep", CancellationToken.None);
            Assert.True(expired >= 1);
        }

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        TemporaryGrantRecord grant = await verification.TemporaryGrants.SingleAsync();
        Assert.Equal(grantId, grant.TemporaryGrantId);
        Assert.NotNull(grant.ExpiredAt);
        Assert.Equal(2, (await verification.EntitlementVersions.SingleAsync()).Version);
    }

    public void Dispose()
    {
        using IdentityAccessDbContext context = CreateContext(_institution);
        context.OutboxMessages.ExecuteDelete();
        context.DeadLetters.ExecuteDelete();
        context.TemporaryGrants.ExecuteDelete();
        context.IdentityRoles.ExecuteDelete();
        context.ConflictRules.ExecuteDelete();
        context.RolePermissions.ExecuteDelete();
        context.Roles.ExecuteDelete();
        context.EntitlementVersions.ExecuteDelete();
        context.InstitutionMemberships.ExecuteDelete();
        context.Identities.ExecuteDelete();

        using IdentityAccessDbContext other = CreateContext(_otherInstitution);
        other.RolePermissions.ExecuteDelete();
        other.Roles.ExecuteDelete();
    }

    private void Seed()
    {
        SeedPermission(UpdatePermission);
        SeedPermission(PublishPermission);
        SeedRole(_institution, RoleId, "Product editor", UpdatePermission);
        SeedRole(_institution, ConflictingRoleId, "Product publisher", PublishPermission);

        using IdentityAccessDbContext context = CreateContext(_institution);
        DateTimeOffset now = _time.GetUtcNow();

        context.Identities.Add(new IdentityRecord
        {
            InstitutionId = _institution.Value,
            IdentityId = _target.Value,
            ActorType = ActorType.Human,
            IdentityType = CoreBanking.IdentityAccess.Domain.Identities.IdentityKind.Staff,
            DisplayName = "Grant test target",
            Status = CoreBanking.IdentityAccess.Domain.Identities.IdentityState.Active,
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyVersion = 1,
        });

        context.ConflictRules.Add(new ConflictRuleRecord
        {
            InstitutionId = _institution.Value,
            ConflictRuleId = "rule-1",
            PermissionCode = UpdatePermission,
            ConflictingPermissionCode = PublishPermission,
            RuleType = "permission_pair",
        });

        context.SaveChanges();
    }

    private void SeedPermission(string code)
    {
        using IdentityAccessDbContext context = CreateContext(_institution);
        if (!context.Permissions.Any(permission => permission.PermissionCode == code))
        {
            context.Permissions.Add(new PermissionRecord
            {
                PermissionCode = code,
                Description = code,
                IntroducedAt = _time.GetUtcNow(),
            });
            context.SaveChanges();
        }
    }

    private void SeedRole(InstitutionId institution, string roleId, string name, string permissionCode)
    {
        using IdentityAccessDbContext context = CreateContext(institution);
        context.Roles.Add(new RoleRecord
        {
            InstitutionId = institution.Value,
            RoleId = roleId,
            RoleCode = roleId,
            RoleName = name,
            Version = 1,
            Status = RecordStatus.Active,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
        });
        context.RolePermissions.Add(new RolePermissionRecord
        {
            InstitutionId = institution.Value,
            RoleId = roleId,
            PermissionCode = permissionCode,
        });
        context.SaveChanges();
    }

    private static async Task<OutboxRecordWrapper> SingleEntitlementEventAsync(IdentityAccessDbContext context)
    {
        var record = await context.OutboxMessages.SingleAsync();
        return new OutboxRecordWrapper(record.EventType, record.Envelope, record.BusinessReference);
    }

    private static long ReadNewVersion(string envelope)
    {
        using JsonDocument document = JsonDocument.Parse(envelope);
        JsonElement payload = FindProperty(document.RootElement, "payload")!.Value;

        // The envelope carries the payload as an already-serialized string.
        if (payload.ValueKind == JsonValueKind.String)
        {
            using JsonDocument payloadDocument = JsonDocument.Parse(payload.GetString()!);
            return FindProperty(payloadDocument.RootElement, "newVersion")!.Value.GetInt64();
        }

        return FindProperty(payload, "newVersion")!.Value.GetInt64();
    }

    private static JsonElement? FindProperty(JsonElement element, string name)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private GrantAdministrationService CreateService(IdentityAccessDbContext context) => new(context, _time);

    private IdentityAccessDbContext CreateContext(InstitutionId? institution)
    {
        AmbientTenantContext tenant = new();
        if (institution is not null)
        {
            tenant.Establish(institution);
        }

        DbContextOptionsBuilder<IdentityAccessDbContext> options = new();
        options.UseNpgsql(_connectionString);

        return new IdentityAccessDbContext(options.Options, tenant);
    }

    private sealed record OutboxRecordWrapper(string EventType, string Envelope, string BusinessReference);

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
