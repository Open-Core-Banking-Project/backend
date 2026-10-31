using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Application.Privacy;
using CoreBanking.CustomerOnboarding.Application.Requirements;
using CoreBanking.CustomerOnboarding.Contracts;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using CoreBanking.CustomerOnboarding.Infrastructure.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Onboarding;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using CoreBanking.CustomerOnboarding.Infrastructure.Privacy;
using CoreBanking.CustomerOnboarding.Infrastructure.Protection;
using CoreBanking.CustomerOnboarding.Infrastructure.Requirements;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Persistence;

/// <summary>
/// Relationship-manager assignments against a real <c>customer_onboarding_db</c>: one current assignment per
/// customer, reassignment closing the prior period, an idempotent repeat, the `RelationshipManagerAssigned.v1`
/// event, audit evidence and tenant scoping. Requirement: CUS-010, CUS-026.
/// </summary>
[Trait("Category", "Database")]
public sealed class RelationshipManagerDatabaseTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("rm-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _officer = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _reviewer = ActorId.From("relationship-reviewer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public RelationshipManagerDatabaseTests()
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
    public async Task An_assignment_records_an_event_and_audit_evidence()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await CustomerAsync(context, "RM-1");
        RelationshipManagerService managers = new(context, _time);

        RelationshipManagerView view = await managers.AssignAsync(Assign(customerId, "staff-rm-1", "branch-1"), CancellationToken.None);

        Assert.NotNull(view.Current);
        RelationshipManagerAssignmentView current = view.Current;
        Assert.Equal("staff-rm-1", current.ManagerActorId);
        Assert.Equal("branch-1", current.ScopeBranchId);
        Assert.Null(current.EffectiveTo);
        Assert.Empty(view.History);

        OutboxRecord outbox = await context.OutboxMessages.SingleAsync(message =>
            message.EventType == CustomerContractV1.RelationshipManagerAssignedEventType);
        Assert.Equal(
            EventContract.TopicFor(CustomerContractV1.Producer, CustomerContractV1.EventFamily, 1),
            outbox.Topic);
        RelationshipManagerAssignedV1 payload =
            EventContract.Deserialize<RelationshipManagerAssignedV1>(EventContract.Deserialize<IntegrationEventEnvelope>(outbox.Envelope)!.Payload)!;
        Assert.Equal(customerId, payload.CustomerId);
        Assert.Equal("staff-rm-1", payload.ManagerActorId);
        Assert.Null(payload.PreviousAssignmentId);

        CustomerAuditEventRecord audit = await context.CustomerAuditEvents.SingleAsync(record =>
            record.Action == "customer.relationship_manager.assigned");
        Assert.Equal(customerId, audit.SubjectReference);
        Assert.Contains("staff-rm-1", audit.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reassignment_closes_the_prior_assignment_and_keeps_it_in_history()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await CustomerAsync(context, "RM-2");
        RelationshipManagerService managers = new(context, _time);

        RelationshipManagerView first = await managers.AssignAsync(Assign(customerId, "staff-rm-1", null), CancellationToken.None);
        _time.Advance(TimeSpan.FromDays(3));
        RelationshipManagerView second = await managers.AssignAsync(Assign(customerId, "staff-rm-2", null), CancellationToken.None);

        Assert.NotNull(second.Current);
        RelationshipManagerAssignmentView current = second.Current;
        Assert.Equal("staff-rm-2", current.ManagerActorId);
        Assert.Equal(first.Current!.AssignmentId, current.PreviousAssignmentId);

        RelationshipManagerAssignmentView previous = Assert.Single(second.History);
        Assert.Equal("staff-rm-1", previous.ManagerActorId);
        Assert.Equal(Start.AddDays(3), previous.EffectiveTo);

        int currentCount = await context.RelationshipManagerAssignments.CountAsync(record => record.EffectiveTo == null);
        Assert.Equal(1, currentCount);
    }

    [Fact]
    public async Task Repeating_the_current_assignment_is_idempotent()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await CustomerAsync(context, "RM-3");
        RelationshipManagerService managers = new(context, _time);

        await managers.AssignAsync(Assign(customerId, "staff-rm-1", null), CancellationToken.None);
        RelationshipManagerView repeated = await managers.AssignAsync(Assign(customerId, "staff-rm-1", null), CancellationToken.None);

        Assert.Single(await context.RelationshipManagerAssignments.ToListAsync());
        Assert.Empty(repeated.History);
        Assert.Single(await context.OutboxMessages
            .Where(message => message.EventType == CustomerContractV1.RelationshipManagerAssignedEventType)
            .ToListAsync());
    }

    [Fact]
    public async Task A_second_current_assignment_is_refused_by_the_database()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await CustomerAsync(context, "RM-4");
        RelationshipManagerService managers = new(context, _time);
        RelationshipManagerView first = await managers.AssignAsync(Assign(customerId, "staff-rm-1", null), CancellationToken.None);

        context.RelationshipManagerAssignments.Add(new RelationshipManagerAssignmentRecord
        {
            InstitutionId = _institution.Value,
            AssignmentId = Guid.NewGuid().ToString("N"),
            CustomerId = customerId,
            ManagerActorId = "staff-rm-2",
            EffectiveFrom = Start,
            AssignedBy = _reviewer.Value,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal("staff-rm-1", first.Current!.ManagerActorId);
    }

    [Fact]
    public async Task Another_institution_cannot_read_or_assign()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await CustomerAsync(context, "RM-5");
        RelationshipManagerService managers = new(context, _time);
        await managers.AssignAsync(Assign(customerId, "staff-rm-1", null), CancellationToken.None);

        await using CustomerOnboardingDbContext otherContext = CreateContext(_otherInstitution);
        RelationshipManagerService other = new(otherContext, _time);

        await Assert.ThrowsAsync<CustomerNotFoundException>(
            () => other.GetAsync(new GetRelationshipManagerQuery(_otherInstitution, _reviewer, customerId), CancellationToken.None));
        Assert.Empty(await otherContext.RelationshipManagerAssignments.ToListAsync());
    }

    [Fact]
    public async Task An_unknown_customer_is_refused()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        RelationshipManagerService managers = new(context, _time);

        await Assert.ThrowsAsync<CustomerNotFoundException>(
            () => managers.AssignAsync(Assign(Guid.NewGuid().ToString("N"), "staff-rm-1", null), CancellationToken.None));
    }

    private AssignRelationshipManagerCommand Assign(string customerId, string managerActorId, string? scopeBranchId) =>
        new(_institution, _reviewer, customerId, managerActorId, scopeBranchId, "regional handover", "correlation");

    private async Task<string> CustomerAsync(CustomerOnboardingDbContext context, string reference)
    {
        await EnsureRequirementPackAsync(context);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);

        OnboardingApplicationView started = await onboarding.StartAsync(
            new StartIndividualApplicationCommand(_institution, _officer, "idem-start-" + reference, reference, "branch-1", CompleteCapture(), "correlation"),
            CancellationToken.None);

        OnboardingApplicationView submitted = await onboarding.SubmitAsync(
            new SubmitOnboardingApplicationCommand(_institution, _officer, "idem-submit-" + reference, started.ApplicationId, started.Revision, "correlation"),
            CancellationToken.None);

        CustomerService customers = new(context, _time);
        CustomerView customer = await customers.RegisterAsync(
            new RegisterCustomerCommand(_institution, _officer, submitted.ApplicationId, null, "correlation"),
            CancellationToken.None);

        return customer.CustomerId;
    }

    private async Task EnsureRequirementPackAsync(CustomerOnboardingDbContext context)
    {
        if (await context.RequirementPacks.AnyAsync(pack => pack.Status == "effective"))
        {
            return;
        }

        RequirementService requirements = new(context, new PendingScanDocumentQuarantine(), _time);
        await requirements.PublishPackAsync(
            new PublishRequirementPackCommand(
                _institution,
                _officer,
                "phase1-test",
                [OnboardingRequirement.Create("contact_detail", "Contact detail", true, ["document"])],
                "correlation"),
            CancellationToken.None);
    }

    private static OnboardingCapture CompleteCapture() =>
        new(
            PersonName.Create("Ada", null, "Lovelace"),
            BirthDetails.Create(new DateOnly(1990, 5, 12), "GB", DateOnly.FromDateTime(Start.UtcDateTime)),
            [ContactPoint.Create(ContactPointType.Email, "ada@example.com")],
            [Address.Create(AddressType.Residential, "12 Analytical Engine Way", null, "London", null, "GB")]);

    private CustomerOnboardingDbContext CreateContext(InstitutionId institution)
    {
        AmbientTenantContext tenant = new();
        tenant.Establish(institution);

        return new CustomerOnboardingDbContext(
            new DbContextOptionsBuilder<CustomerOnboardingDbContext>().UseNpgsql(_connectionString).Options,
            tenant);
    }

    private OnboardingApplicationService CreateOnboardingService(CustomerOnboardingDbContext context) =>
        new(
            context,
            new AesGcmProtectedValueProtector(CreateProtectionOptions()),
            new IdempotencyService(context, _time),
            _time);

    private static ProtectedValueProtectionOptions CreateProtectionOptions() =>
        ProtectedValueProtectionOptions.Create(
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)(32 + index)).ToArray()),
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)(96 + index)).ToArray()));

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
