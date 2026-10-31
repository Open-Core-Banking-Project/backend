using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CustomerOnboarding.Application.Audit;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Application.Kyc;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Application.Privacy;
using CoreBanking.CustomerOnboarding.Application.Requirements;
using CoreBanking.CustomerOnboarding.Domain.Kyc;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;
using CoreBanking.CustomerOnboarding.Domain.Privacy;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using CoreBanking.CustomerOnboarding.Infrastructure.Audit;
using CoreBanking.CustomerOnboarding.Infrastructure.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Kyc;
using CoreBanking.CustomerOnboarding.Infrastructure.Onboarding;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using CoreBanking.CustomerOnboarding.Infrastructure.Privacy;
using CoreBanking.CustomerOnboarding.Infrastructure.Protection;
using CoreBanking.CustomerOnboarding.Infrastructure.Requirements;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Persistence;

/// <summary>
/// Immutable audit evidence and the reconciliation pass against a real <c>customer_onboarding_db</c>: every audited
/// action appends a row and a SIEM event in the same transaction, and the pass reports deterministic integrity
/// violations without repairing them. Decision: P1-E03-D14.
/// </summary>
[Trait("Category", "Database")]
public sealed class CustomerAuditDatabaseTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 29, 21, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("audit-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _officer = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _reviewer = ActorId.From("audit-reviewer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public CustomerAuditDatabaseTests()
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
    public async Task An_activation_appends_audit_evidence_and_a_siem_event()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);

        CustomerView customer = await ActiveCustomerAsync(context, customers, "AUD-1");

        List<CustomerAuditEventRecord> audit = await context.CustomerAuditEvents
            .Where(record => record.Action == "customer.status.changed")
            .ToListAsync();
        CustomerAuditEventRecord activation = Assert.Single(audit);
        Assert.Equal("customer", activation.SubjectType);
        Assert.Equal(customer.CustomerId, activation.SubjectReference);
        Assert.Contains("\"to\":\"active\"", activation.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("ada@example.com", activation.Detail, StringComparison.OrdinalIgnoreCase);

        List<OutboxRecord> siemEvents = await context.OutboxMessages
            .Where(message => message.EventType == "customer_audit_event")
            .ToListAsync();
        Assert.All(siemEvents, message => Assert.Equal("corebanking.customer-onboarding.audit.v1", message.Topic));

        OutboxRecord siemEvent = Assert.Single(
            siemEvents,
            message => EventContract.Deserialize<IntegrationEventEnvelope>(message.Envelope)!
                .Payload.Contains(activation.AuditEventId, StringComparison.Ordinal));
        IntegrationEventEnvelope envelope = EventContract.Deserialize<IntegrationEventEnvelope>(siemEvent.Envelope)!;
        Assert.Equal(_institution.Value, envelope.InstitutionId);
    }

    [Fact]
    public async Task Every_audited_action_appends_its_own_evidence()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        CustomerMergeService merges = new(context, _time);

        CustomerView survivor = await ActiveCustomerAsync(context, customers, "AUD-2-A");
        CustomerView source = await ActiveCustomerAsync(context, customers, "AUD-2-B");

        CustomerPreferencesService preferences = new(context, _time);
        await preferences.SetAsync(
            new SetCustomerPreferenceCommand(
                _institution,
                _reviewer,
                survivor.CustomerId,
                "marketing",
                "mobile_app",
                PreferenceState.Permitted,
                "consent",
                "customer",
                null,
                null,
                "correlation"),
            CancellationToken.None);

        CustomerMergeView proposed = await merges.ProposeAsync(
            new ProposeCustomerMergeCommand(
                _institution,
                _reviewer,
                survivor.CustomerId,
                source.CustomerId,
                "same person confirmed by duplicate review",
                ["core-banking", "risk-reporting"],
                "correlation"),
            CancellationToken.None);
        await merges.ApproveAsync(
            new ApproveCustomerMergeCommand(_institution, _reviewer, proposed.MergeRequestId, "workflow-merge-1", "correlation"),
            CancellationToken.None);
        await merges.ApplyAsync(
            new ApplyCustomerMergeCommand(_institution, _reviewer, proposed.MergeRequestId, "correlation"),
            CancellationToken.None);

        List<string> actions = await context.CustomerAuditEvents
            .Select(record => record.Action)
            .ToListAsync();

        Assert.Contains("requirement.pack.published", actions);
        Assert.Contains("customer.status.changed", actions);
        Assert.Contains("customer.classification.approved", actions);
        Assert.Contains("kyc.requirement.waived", actions);
        Assert.Contains("customer.preference.set", actions);
        Assert.Contains("customer.merge.applied", actions);
        Assert.Equal(actions.Count, await context.OutboxMessages.CountAsync(message => message.EventType == "customer_audit_event"));
    }

    [Fact]
    public async Task A_healthy_state_produces_no_reconciliation_findings()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);

        await ActiveCustomerAsync(context, customers, "AUD-3");

        CustomerReconciliationReport report = await ReconcileAsync(context);

        Assert.DoesNotContain(report.Findings, finding => finding.InstitutionId == _institution.Value);
        Assert.Equal(report.Findings.Count, report.TotalFindings);
    }

    [Fact]
    public async Task Reconciliation_reports_each_integrity_violation_without_repairing_it()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);

        CustomerView customer = await ActiveCustomerAsync(context, customers, "AUD-4");

        context.MergeRequests.Add(new MergeRequestRecord
        {
            InstitutionId = _institution.Value,
            MergeRequestId = Guid.NewGuid().ToString("N"),
            SurvivorCustomerId = customer.CustomerId,
            SourceCustomerId = Guid.NewGuid().ToString("N"),
            Reason = "seeded inconsistency",
            PlanHash = new string('a', 64),
            Status = "reconciliation_required",
            RequiredConsumers = "[\"core-banking\"]",
            ApprovalReference = "workflow-seeded",
            CreatedAt = Start,
            UpdatedAt = Start,
        });

        context.CustomerStatusHistory.Add(new CustomerStatusHistoryRecord
        {
            InstitutionId = _institution.Value,
            CustomerId = customer.CustomerId,
            Sequence = 99,
            FromStatus = "active",
            ToStatus = "closed",
            Reason = "seeded without approval",
            ActorId = _reviewer.Value,
            ApprovalReference = null,
            OccurredAt = Start,
        });

        KycCaseRecord kycCase = await context.KycCases.SingleAsync();
        KycRequirementRecord requirement = await context.KycRequirements
            .FirstAsync(record => record.CaseId == kycCase.CaseId);
        requirement.Status = "in_progress";
        requirement.LinkedVerificationId = null;
        requirement.WaivedAt = null;
        requirement.WaiverAuthority = null;
        requirement.WaiverReason = null;

        context.CustomerAliases.Add(new CustomerAliasRecord
        {
            InstitutionId = _institution.Value,
            AliasCustomerId = Guid.NewGuid().ToString("N"),
            SurvivorCustomerId = Guid.NewGuid().ToString("N"),
            MergeRequestId = Guid.NewGuid().ToString("N"),
            Reason = "seeded dangling alias",
            CreatedBy = _reviewer.Value,
            CreatedAt = Start,
        });

        await context.SaveChangesAsync();

        CustomerReconciliationReport report = await ReconcileAsync(context);

        Assert.Contains(CustomerReconciliationService.MergeAwaitingConsumers, report.FindingsByCode.Keys);
        Assert.Contains(CustomerReconciliationService.MergeAppliedWithoutAlias, report.FindingsByCode.Keys);
        Assert.Contains(CustomerReconciliationService.TransitionWithoutApproval, report.FindingsByCode.Keys);
        Assert.Contains(CustomerReconciliationService.KycRequirementWithoutVerification, report.FindingsByCode.Keys);
        Assert.Contains(CustomerReconciliationService.AliasTargetMissing, report.FindingsByCode.Keys);
        Assert.Equal(report.Findings.Count, report.TotalFindings);

        Assert.Equal(
            "reconciliation_required",
            (await context.MergeRequests.SingleAsync(request => request.Reason == "seeded inconsistency")).Status);
    }

    [Fact]
    public async Task Audit_rows_are_tenant_scoped()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        CustomerService customers = new(context, _time);
        await ActiveCustomerAsync(context, customers, "AUD-5");

        await using CustomerOnboardingDbContext otherContext = CreateContext(_otherInstitution);
        Assert.Empty(await otherContext.CustomerAuditEvents.ToListAsync());
    }

    private static Task<CustomerReconciliationReport> ReconcileAsync(CustomerOnboardingDbContext context) =>
        new CustomerReconciliationService(context).ReconcileAsync(Start, CancellationToken.None);

    private async Task<CustomerView> ActiveCustomerAsync(
        CustomerOnboardingDbContext context,
        CustomerService customers,
        string reference)
    {
        (string applicationId, int revision) = await SubmittedAsync(context, reference);
        CustomerView customer = await customers.RegisterAsync(
            new RegisterCustomerCommand(_institution, _officer, applicationId, null, "correlation"),
            CancellationToken.None);

        await customers.ProposeClassificationAsync(
            new ProposeClassificationCommand(_institution, _reviewer, customer.CustomerId, "tier_1", "retail", "low", false, "correlation"),
            CancellationToken.None);
        await customers.ApproveClassificationAsync(
            new ApproveClassificationCommand(_institution, _reviewer, customer.CustomerId, 1, "workflow-classification-1", "correlation"),
            CancellationToken.None);

        await CompleteKycCaseAsync(context, applicationId, revision);

        return await customers.ActivateAsync(
            new ActivateCustomerCommand(_institution, _reviewer, customer.CustomerId, 1, "correlation"),
            CancellationToken.None);
    }

    private async Task<KycCaseView> CompleteKycCaseAsync(
        CustomerOnboardingDbContext context,
        string applicationId,
        int revision)
    {
        KycCaseService kyc = new(context, _time);
        KycCaseView opened = await kyc.OpenAsync(
            new OpenKycCaseCommand(_institution, _reviewer, applicationId, revision, "correlation"),
            CancellationToken.None);

        KycCaseView current = opened;
        foreach (KycRequirementView requirement in opened.Requirements)
        {
            current = await kyc.WaiveRequirementAsync(
                new WaiveKycRequirementCommand(_institution, _reviewer, opened.CaseId, requirement.Code, "evidence not available in this channel", "correlation"),
                CancellationToken.None);
        }

        return current;
    }

    private async Task<(string ApplicationId, int Revision)> SubmittedAsync(
        CustomerOnboardingDbContext context,
        string reference)
    {
        await EnsureRequirementPackAsync(context);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);

        OnboardingApplicationView started = await onboarding.StartAsync(
            new StartIndividualApplicationCommand(_institution, _officer, "idem-start-" + reference, reference, "branch-1", CompleteCapture(), "correlation"),
            CancellationToken.None);

        OnboardingApplicationView submitted = await onboarding.SubmitAsync(
            new SubmitOnboardingApplicationCommand(_institution, _officer, "idem-submit-" + reference, started.ApplicationId, started.Revision, "correlation"),
            CancellationToken.None);

        return (submitted.ApplicationId, submitted.Revision);
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

    /// <summary>The audit event type lives in the contracts assembly; this keeps the test independent of the constant.</summary>
    private static class CustomerContractV1Proxy
    {
        public const string AuditEventType = "customer_audit_event";
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
