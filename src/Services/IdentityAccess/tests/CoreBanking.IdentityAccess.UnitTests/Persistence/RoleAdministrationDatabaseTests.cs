using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
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
    public async Task Submission_freezes_the_exact_version_and_durably_requests_workflow_approval_once()
    {
        string roleId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            roleId = (await CreateService(context).CreateDraftAsync(
                _institution,
                _admin,
                "publication-candidate",
                "Publication candidate",
                ["audit.event.view"],
                CancellationToken.None)).RoleId;
        }

        RoleSubmissionResultV1 first;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            first = await CreateService(context).SubmitAsync(
                _institution, _admin, roleId, "role-correlation-1", CancellationToken.None);
            Assert.Single(
                context.OperatorActionAuditEvents.Local,
                record => record.ResourceId == roleId && record.Action == "role_submitted_for_approval");
        }

        Assert.Equal(RoleContractV1.StatusPendingApproval, first.Status);
        Assert.Equal(64, first.ContentHash.Length);

        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            RoleSubmissionResultV1 replay = await CreateService(context).SubmitAsync(
                _institution, _admin, roleId, "role-correlation-2", CancellationToken.None);

            Assert.Equal(first, replay);
            OutboxRecord message = await context.OutboxMessages.SingleAsync(
                record => record.BusinessReference == first.ApprovalRequestId);
            IntegrationEventEnvelope envelope = EventContract.Deserialize<IntegrationEventEnvelope>(message.Envelope)!;
            RoleApprovalRequestedV1 payload = EventContract.Deserialize<RoleApprovalRequestedV1>(envelope.Payload)!;

            Assert.Equal(RolePublicationApprovalV1.RequestedEventType, message.EventType);
            Assert.Equal(RolePublicationApprovalV1.WorkflowKey, payload.WorkflowKey);
            Assert.Equal(RolePublicationApprovalV1.DefinitionHash, payload.WorkflowDefinitionHash);
            Assert.Equal(_admin.Value, payload.MakerActorId);
            Assert.Equal(roleId, payload.SubjectId);
            Assert.Equal("1", payload.SubjectVersion);
            Assert.Equal(first.ContentHash, payload.SubjectHash);
            Assert.Equal("role-correlation-1", payload.CorrelationId);
        }
    }

    [Fact]
    public async Task A_submitted_role_cannot_be_changed_while_workflow_is_deciding()
    {
        string roleId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            RoleAdministrationService service = CreateService(context);
            roleId = (await service.CreateDraftAsync(
                _institution, _admin, "frozen-role", "Frozen role", ["audit.event.view"], CancellationToken.None)).RoleId;
            await service.SubmitAsync(_institution, _admin, roleId, "role-correlation-3", CancellationToken.None);
        }

        await using IdentityAccessDbContext updateContext = CreateContext(_institution);
        RoleAdministrationException exception = await Assert.ThrowsAsync<RoleAdministrationException>(() =>
            CreateService(updateContext).UpdateDraftAsync(
                _institution, roleId, "Changed", ["identity.identity.view"], CancellationToken.None));

        Assert.Equal(RoleContractV1.RoleNotDraft, exception.Code);
    }

    [Fact]
    public async Task Concurrent_submissions_converge_on_one_frozen_request_and_one_outbox_fact()
    {
        string roleId;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            roleId = (await CreateService(context).CreateDraftAsync(
                _institution, _admin, "concurrent-role", "Concurrent role", ["audit.event.view"], CancellationToken.None)).RoleId;
        }

        async Task<RoleSubmissionResultV1> SubmitAsync(string correlationId)
        {
            await using IdentityAccessDbContext context = CreateContext(_institution);
            return await CreateService(context).SubmitAsync(
                _institution, _admin, roleId, correlationId, CancellationToken.None);
        }

        RoleSubmissionResultV1[] results = await Task.WhenAll(
            SubmitAsync("role-concurrent-1"),
            SubmitAsync("role-concurrent-2"));

        Assert.Equal(results[0].ApprovalRequestId, results[1].ApprovalRequestId);
        Assert.Equal(results[0].ContentHash, results[1].ContentHash);

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        Assert.Equal(1, await verification.OutboxMessages.CountAsync(
            message => message.EventType == RolePublicationApprovalV1.RequestedEventType
                && message.BusinessReference == results[0].ApprovalRequestId));
    }

    [Fact]
    public async Task A_trusted_workflow_decision_publishes_the_exact_role_and_is_inbox_idempotent()
    {
        string roleId;
        RoleSubmissionResultV1 submission;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            RoleAdministrationService service = CreateService(context);
            roleId = (await service.CreateDraftAsync(
                _institution, _admin, "approved-role", "Approved role", ["audit.event.view"], CancellationToken.None)).RoleId;
            submission = await service.SubmitAsync(
                _institution, _admin, roleId, "role-correlation-4", CancellationToken.None);
        }

        ActorId approver = ActorId.From("role-approver-" + Guid.NewGuid().ToString("N")[..12]);
        IntegrationEventEnvelope decision = DecisionEnvelope(submission, approver, "approved", "decision-approved-1");
        AmbientTenantContext tenant = new();
        await using (IdentityAccessDbContext context = CreateContext(_institution, tenant))
        {
            RoleApprovalDecidedConsumer consumer = new(context, new InboxProcessor(context, _time), tenant, _time);
            Assert.Equal(InboxResult.Processed, await consumer.HandleAsync(decision, CancellationToken.None));
            Assert.Equal(InboxResult.Duplicate, await consumer.HandleAsync(decision, CancellationToken.None));
        }

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        RoleRecord role = await verification.Roles.SingleAsync(record => record.RoleId == roleId);
        Assert.Equal(RecordStatus.Active, role.Status);
        Assert.Equal("decision-approved-1", role.WorkflowDecisionReference);
        Assert.Equal(approver.Value, role.DecidedBy);
        Assert.NotNull(role.PublishedAt);
        RoleVersionRecord published = await verification.RoleVersions.SingleAsync(
            version => version.RoleId == roleId && version.Version == 1);
        Assert.Equal(submission.ContentHash, published.ContentHash);
        Assert.Equal(["audit.event.view"], await verification.RoleVersionPermissions
            .Where(permission => permission.RoleId == roleId && permission.RoleVersion == 1)
            .Select(permission => permission.PermissionCode)
            .ToArrayAsync());
        Assert.Equal(1, await verification.OutboxMessages.CountAsync(
            message => message.EventType == RolePublicationApprovalV1.PublishedEventType
                && message.BusinessReference == roleId));
        Assert.Equal(1, await verification.OutboxMessages.CountAsync(
            message => message.EventType == RolePublicationApprovalV1.ApplicationResultEventType
                && message.BusinessReference == submission.ApprovalRequestId));
    }

    [Fact]
    public async Task A_maker_cannot_publish_their_own_role_even_if_the_event_claims_approval()
    {
        string roleId;
        RoleSubmissionResultV1 submission;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            RoleAdministrationService service = CreateService(context);
            roleId = (await service.CreateDraftAsync(
                _institution, _admin, "self-approved-role", "Self approved role", ["audit.event.view"], CancellationToken.None)).RoleId;
            submission = await service.SubmitAsync(
                _institution, _admin, roleId, "role-correlation-5", CancellationToken.None);
        }

        AmbientTenantContext tenant = new();
        await using (IdentityAccessDbContext context = CreateContext(_institution, tenant))
        {
            RoleApprovalDecidedConsumer consumer = new(context, new InboxProcessor(context, _time), tenant, _time);
            Assert.Equal(
                InboxResult.Deferred,
                await consumer.HandleAsync(
                    DecisionEnvelope(submission, _admin, "approved", "decision-self-1"),
                    CancellationToken.None));
        }

        await using IdentityAccessDbContext verification = CreateContext(_institution);
        RoleRecord role = await verification.Roles.SingleAsync(record => record.RoleId == roleId);
        Assert.Equal(RecordStatus.Pending, role.Status);
        Assert.Null(role.WorkflowDecisionReference);
        Assert.NotNull(role.ApprovalRequestId);
    }

    [Fact]
    public async Task A_rejected_decision_retains_evidence_and_returns_the_role_to_editable_draft()
    {
        string roleId;
        RoleSubmissionResultV1 submission;
        await using (IdentityAccessDbContext context = CreateContext(_institution))
        {
            RoleAdministrationService service = CreateService(context);
            roleId = (await service.CreateDraftAsync(
                _institution, _admin, "rejected-role", "Rejected role", ["audit.event.view"], CancellationToken.None)).RoleId;
            submission = await service.SubmitAsync(
                _institution, _admin, roleId, "role-correlation-6", CancellationToken.None);
        }

        ActorId approver = ActorId.From("role-approver-" + Guid.NewGuid().ToString("N")[..12]);
        AmbientTenantContext tenant = new();
        await using (IdentityAccessDbContext context = CreateContext(_institution, tenant))
        {
            RoleApprovalDecidedConsumer consumer = new(context, new InboxProcessor(context, _time), tenant, _time);
            Assert.Equal(
                InboxResult.Processed,
                await consumer.HandleAsync(
                    DecisionEnvelope(submission, approver, "rejected", "decision-rejected-1"),
                    CancellationToken.None));
        }

        await using IdentityAccessDbContext updateContext = CreateContext(_institution);
        RoleRecord rejected = await updateContext.Roles.SingleAsync(record => record.RoleId == roleId);
        Assert.Equal(RecordStatus.Pending, rejected.Status);
        Assert.Equal("rejected", rejected.Decision);
        Assert.Equal("decision-rejected-1", rejected.WorkflowDecisionReference);
        Assert.Null(rejected.ApprovalRequestId);

        RoleDraftResultV1 updated = await CreateService(updateContext).UpdateDraftAsync(
            _institution, roleId, "Corrected role", ["identity.identity.view"], CancellationToken.None);
        Assert.Equal(2, updated.Version);
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
        context.InboxMessages.ExecuteDelete();
        context.OutboxMessages.ExecuteDelete();
        context.RoleVersionPermissions.ExecuteDelete();
        context.RoleVersions.ExecuteDelete();
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

    private IntegrationEventEnvelope DecisionEnvelope(
        RoleSubmissionResultV1 submission,
        ActorId approver,
        string decision,
        string decisionId)
    {
        DateTimeOffset now = _time.GetUtcNow();
        RoleApprovalDecidedV1 payload = new(
            _institution.Value,
            submission.ApprovalRequestId,
            decisionId,
            decision,
            RolePublicationApprovalV1.WorkflowKey,
            RolePublicationApprovalV1.WorkflowVersionId,
            RolePublicationApprovalV1.DefinitionHash,
            RolePublicationApprovalV1.SubjectType,
            submission.RoleId,
            submission.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            submission.ContentHash,
            approver.Value,
            RolePublicationApprovalV1.RequiredPermission,
            "entitlement-1",
            now.AddMinutes(-1),
            now.AddMinutes(4),
            Reason: null,
            "role-correlation-decision",
            now);

        return new IntegrationEventEnvelope
        {
            EventId = Guid.NewGuid().ToString("N"),
            EventType = RolePublicationApprovalV1.DecidedEventType,
            SchemaVersion = RoleContractV1.SchemaVersion,
            OccurredAt = now,
            InstitutionId = _institution.Value,
            CorrelationId = payload.CorrelationId,
            Producer = RoleApprovalDecidedConsumer.SourceProducer,
            BusinessReference = submission.ApprovalRequestId,
            Payload = EventContract.Serialize(payload),
        };
    }

    private IdentityAccessDbContext CreateContext(InstitutionId institution) =>
        CreateContext(institution, new AmbientTenantContext());

    private IdentityAccessDbContext CreateContext(InstitutionId institution, AmbientTenantContext tenant)
    {
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
