using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Access;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Identity;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Platform;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CoreBanking.IdentityAccess.Infrastructure.Persistence;

/// <summary>
/// The persistence model of <c>identity_access_db</c>. Two rules are enforced here rather than in
/// every query: every tenant-owned entity carries a mandatory institution query filter, and the
/// filter fails closed when no trusted institution context has been established.
/// Decision: docs/adr/ADR-0004-database-per-service-and-tenant-isolation.md
/// </summary>
public sealed class IdentityAccessDbContext : DbContext, IIntegrationStore
{
    private readonly ITenantContext _tenantContext;

    public IdentityAccessDbContext(DbContextOptions<IdentityAccessDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext ?? throw new System.ArgumentNullException(nameof(tenantContext));
    }

    public DbSet<IdentityRecord> Identities => Set<IdentityRecord>();

    public DbSet<ServiceClientRecord> ServiceClients => Set<ServiceClientRecord>();

    public DbSet<SessionRecord> Sessions => Set<SessionRecord>();

    public DbSet<RoleRecord> Roles => Set<RoleRecord>();

    public DbSet<PermissionRecord> Permissions => Set<PermissionRecord>();

    public DbSet<RolePermissionRecord> RolePermissions => Set<RolePermissionRecord>();

    public DbSet<IdentityRoleRecord> IdentityRoles => Set<IdentityRoleRecord>();

    public DbSet<ResourceScopeRecord> ResourceScopes => Set<ResourceScopeRecord>();

    public DbSet<PermissionConditionRecord> PermissionConditions => Set<PermissionConditionRecord>();

    public DbSet<ConflictRuleRecord> ConflictRules => Set<ConflictRuleRecord>();

    public DbSet<TemporaryGrantRecord> TemporaryGrants => Set<TemporaryGrantRecord>();

    public DbSet<EntitlementVersionRecord> EntitlementVersions => Set<EntitlementVersionRecord>();

    public DbSet<AuthenticationEventRecord> AuthenticationEvents => Set<AuthenticationEventRecord>();

    public DbSet<AuthorisationAuditRecord> AuthorisationAuditEvents => Set<AuthorisationAuditRecord>();

    public DbSet<OperatorActionAuditRecord> OperatorActionAuditEvents => Set<OperatorActionAuditRecord>();

    public DbSet<OutboxRecord> OutboxMessages => Set<OutboxRecord>();

    public DbSet<InboxRecord> InboxMessages => Set<InboxRecord>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<DeadLetterRecord> DeadLetters => Set<DeadLetterRecord>();

    public DbSet<FoundationRecord> FoundationRecords => Set<FoundationRecord>();

    public DbSet<InstitutionBoundaryRecord> InstitutionBoundaries => Set<InstitutionBoundaryRecord>();

    /// <summary>
    /// The institution of the current operation, read as a parameter by every tenant query filter.
    /// A block-bodied property is required: a null-propagating expression cannot be placed in an
    /// expression tree.
    /// </summary>
    internal string? CurrentInstitutionId
    {
        get
        {
            InstitutionId? current = _tenantContext.CurrentInstitution;
            return current is null ? null : current.Value;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        System.ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityAccessDbContext).Assembly);
        modelBuilder.ApplyIntegrationRecords();

        modelBuilder.Entity<IdentityRecord>().HasQueryFilter(identity => CurrentInstitutionId != null && identity.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<ServiceClientRecord>().HasQueryFilter(client => CurrentInstitutionId != null && client.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<SessionRecord>().HasQueryFilter(session => CurrentInstitutionId != null && session.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<RoleRecord>().HasQueryFilter(role => CurrentInstitutionId != null && role.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<RolePermissionRecord>().HasQueryFilter(grant => CurrentInstitutionId != null && grant.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<IdentityRoleRecord>().HasQueryFilter(assignment => CurrentInstitutionId != null && assignment.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<ResourceScopeRecord>().HasQueryFilter(scope => CurrentInstitutionId != null && scope.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<PermissionConditionRecord>().HasQueryFilter(condition => CurrentInstitutionId != null && condition.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<ConflictRuleRecord>().HasQueryFilter(rule => CurrentInstitutionId != null && rule.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<TemporaryGrantRecord>().HasQueryFilter(grant => CurrentInstitutionId != null && grant.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<EntitlementVersionRecord>().HasQueryFilter(version => CurrentInstitutionId != null && version.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<AuthenticationEventRecord>().HasQueryFilter(evidence => CurrentInstitutionId != null && evidence.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<OperatorActionAuditRecord>().HasQueryFilter(evidence => CurrentInstitutionId != null && evidence.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<OutboxRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<InboxRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<IdempotencyRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<DeadLetterRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<FoundationRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
        modelBuilder.Entity<InstitutionBoundaryRecord>().HasQueryFilter(record => CurrentInstitutionId != null && record.InstitutionId == CurrentInstitutionId);
    }

    /// <summary>Opens the transaction that makes source state and outbox record atomic.</summary>
    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);
}
