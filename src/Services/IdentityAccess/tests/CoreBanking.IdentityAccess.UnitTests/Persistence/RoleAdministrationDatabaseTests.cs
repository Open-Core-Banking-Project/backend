using System;
using System.Linq;
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
/// Role drafting and simulation against a real <c>identity_access_db</c>, using the catalogue the
/// seed migration installs. Excluded from the default run; executed by <c>scripts/db-verify.ps1</c>.
/// Decision: P1-E02-D09 and P1-E02-D10.
/// </summary>
[Trait("Category", "Database")]
public sealed class RoleAdministrationDatabaseTests : IDisposable
{
    private const string ConnectionStringVariableName = "ConnectionStrings__IdentityAccess";
    private const string UpdatePermission = "product.version.update";
    private const string PublishPermission = "product.version.publish";

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("role-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("role-other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _admin = ActorId.From("role-admin-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

    public RoleAdministrationDatabaseTests()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringVariableName} to the application principal connection string, or run scripts/db-verify.ps1.");
        }

        _connectionString = configured;
    }

    [Fact]
    public async Task A_draft_is_created_inert_with_the_seeded_catalogue()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);

        RoleDraftResultV1 result = await CreateService(context).CreateDraftAsync(
            _institution,
            _admin,
            "Product-Manager",
            "Product manager",
            [PublishPermission, "product.version.simulate", PublishPermission],
            CancellationToken.None);

        Assert.Equal("product-manager", result.RoleCode);
        Assert.Equal(RoleContractV1.StatusDraft, result.Status);
        Assert.Equal(1, result.Version);
        Assert.Equal(["product.version.publish", "product.version.simulate"], result.Permissions);

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        Assert.Equal(RecordStatus.Pending, (await verification.Roles.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_draft_can_never_be_granted_before_publication()
    {
        string roleId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            roleId = (await CreateService(context).CreateDraftAsync(
                _institution, _admin, "draft-role", "Draft role", [UpdatePermission], CancellationToken.None)).RoleId;
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            GrantAdministrationException exception = await Assert.ThrowsAsync<GrantAdministrationException>(() =>
                new GrantAdministrationService(context, _time).AssignRoleAsync(
                    _institution, _admin, "some-identity", roleId, null, null, null, null, "correlation-1", CancellationToken.None));
            Assert.Equal(GrantContractV1.RoleNotFound, exception.Code);
        }
    }

    [Fact]
    public async Task A_duplicate_role_code_is_refused()
    {
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            await CreateService(context).CreateDraftAsync(
                _institution, _admin, "audit-reader", "Audit reader", ["audit.event.view"], CancellationToken.None);
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            RoleAdministrationException exception = await Assert.ThrowsAsync<RoleAdministrationException>(() =>
                CreateService(context).CreateDraftAsync(
                    _institution, _admin, "AUDIT-READER", "Another reader", ["audit.event.view"], CancellationToken.None));
            Assert.Equal(RoleContractV1.DuplicateRole, exception.Code);
        }
    }

    [Fact]
    public async Task A_permission_outside_the_catalogue_is_refused()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);

        RoleAdministrationException exception = await Assert.ThrowsAsync<RoleAdministrationException>(() =>
            CreateService(context).CreateDraftAsync(
                _institution, _admin, "unknown-permission", "Unknown", ["invented.permission.code"], CancellationToken.None));

        Assert.Equal(RoleContractV1.PermissionNotInCatalogue, exception.Code);
    }

    [Fact]
    public async Task A_role_that_conflicts_with_itself_is_refused()
    {
        SeedConflictRule();

        await using IdentityAccessDbContext context = CreateContext(_institution);

        RoleAdministrationException exception = await Assert.ThrowsAsync<RoleAdministrationException>(() =>
            CreateService(context).CreateDraftAsync(
                _institution, _admin, "self-conflicting", "Conflicting", [UpdatePermission, PublishPermission], CancellationToken.None));

        Assert.Equal(RoleContractV1.RoleConflictsWithItself, exception.Code);
    }

    [Fact]
    public async Task Updating_a_draft_replaces_permissions_and_advances_its_version()
    {
        string roleId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            roleId = (await CreateService(context).CreateDraftAsync(
                _institution, _admin, "ops-officer", "Operations officer", ["audit.event.view"], CancellationToken.None)).RoleId;
        }

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            RoleDraftResultV1 updated = await CreateService(context).UpdateDraftAsync(
                _institution, roleId, "Operations officer v2", ["identity.identity.view"], CancellationToken.None);

            Assert.Equal(2, updated.Version);
            Assert.Equal(["identity.identity.view"], updated.Permissions);
        }

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        Assert.Single(verification.RolePermissions);
    }

    [Fact]
    public async Task Simulation_reports_conflicts_unknown_codes_and_publishability()
    {
        SeedConflictRule();

        await using IdentityAccessDbContext context = CreateContext(_institution);
        RoleAdministrationService service = CreateService(context);

        RoleSimulationResultV1 conflicting = await service.SimulateAsync(
            [UpdatePermission, PublishPermission, "invented.permission.code"], CancellationToken.None);
        Assert.False(conflicting.IsPublishable);
        Assert.Single(conflicting.Conflicts);
        Assert.Equal(["invented.permission.code"], conflicting.UnknownPermissions);

        RoleSimulationResultV1 clean = await service.SimulateAsync([UpdatePermission, "product.version.simulate"], CancellationToken.None);
        Assert.True(clean.IsPublishable);
        Assert.Empty(clean.Conflicts);
    }

    [Fact]
    public async Task A_role_in_another_institution_is_invisible()
    {
        string roleId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            roleId = (await CreateService(context).CreateDraftAsync(
                _institution, _admin, "tenant-role", "Tenant role", ["audit.event.view"], CancellationToken.None)).RoleId;
        }

        await using IdentityAccessDbContext otherContext = CreateContext(_otherInstitution);

        RoleAdministrationException exception = await Assert.ThrowsAsync<RoleAdministrationException>(() =>
            CreateService(otherContext).GetAsync(_otherInstitution, roleId, CancellationToken.None));

        Assert.Equal(RoleContractV1.RoleNotFound, exception.Code);
    }

    public void Dispose()
    {
        using IdentityAccessDbContext context = CreateContext(_institution);
        context.RolePermissions.ExecuteDelete();
        context.Roles.ExecuteDelete();
        context.ConflictRules.ExecuteDelete();
    }

    private void SeedConflictRule()
    {
        using IdentityAccessDbContext context = CreateContext(_institution);
        context.ConflictRules.Add(new ConflictRuleRecord
        {
            InstitutionId = _institution.Value,
            ConflictRuleId = "role-rule-1",
            PermissionCode = UpdatePermission,
            ConflictingPermissionCode = PublishPermission,
            RuleType = "permission_pair",
        });
        context.SaveChanges();
    }

    private RoleAdministrationService CreateService(IdentityAccessDbContext context) => new(context, _time);

    private IdentityAccessDbContext CreateContext(InstitutionId institution)
    {
        AmbientTenantContext tenant = new();
        tenant.Establish(institution);

        DbContextOptionsBuilder<IdentityAccessDbContext> options = new();
        options.UseNpgsql(_connectionString);

        return new IdentityAccessDbContext(options.Options, tenant);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
