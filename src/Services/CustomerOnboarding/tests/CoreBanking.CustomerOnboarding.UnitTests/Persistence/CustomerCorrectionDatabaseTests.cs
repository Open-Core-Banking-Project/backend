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
using CoreBanking.CustomerOnboarding.Application.Kyc;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Application.Requirements;
using CoreBanking.CustomerOnboarding.Contracts;
using CoreBanking.CustomerOnboarding.Domain.Kyc;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using CoreBanking.CustomerOnboarding.Infrastructure.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Kyc;
using CoreBanking.CustomerOnboarding.Infrastructure.Onboarding;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using CoreBanking.CustomerOnboarding.Infrastructure.Protection;
using CoreBanking.CustomerOnboarding.Infrastructure.Requirements;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Persistence;

/// <summary>
/// Protected-data corrections against a real <c>customer_onboarding_db</c>: the correction is append-only, keeps
/// the superseded reference, invalidates the affected verification so it is never treated as current, publishes
/// a field-code-only event and never writes a protected value. Decision: epic 03 CUS-UC-06, invariant 15.
/// </summary>
[Trait("Category", "Database")]
public sealed class CustomerCorrectionDatabaseTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 30, 18, 30, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("corr-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _officer = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _steward = ActorId.From("data-steward-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public CustomerCorrectionDatabaseTests()
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
    public async Task A_correction_keeps_the_prior_reference_and_records_evidence()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await CustomerAsync(context, "CORR-1");

        CustomerCorrectionView view = await ServiceAsync(context).RecordAsync(Correction(customerId), CancellationToken.None);

        CustomerCorrectionRecord stored = await context.CustomerCorrections.SingleAsync();
        Assert.Equal("ref-prior-1", stored.PriorValueReference);
        Assert.Equal("ref-new-1", stored.NewValueReference);
        Assert.False(stored.RequiresReverification);
        Assert.Equal(view.CorrectionId, stored.CorrectionId);

        OutboxRecord outbox = await context.OutboxMessages.SingleAsync(message =>
            message.EventType == CustomerContractV1.CustomerProfileChangedEventType);
        Assert.Equal(
            EventContract.TopicFor(CustomerContractV1.Producer, CustomerContractV1.EventFamily, 1),
            outbox.Topic);
        IntegrationEventEnvelope envelope = EventContract.Deserialize<IntegrationEventEnvelope>(outbox.Envelope)!;
        CustomerProfileChangedV1 payload = EventContract.Deserialize<CustomerProfileChangedV1>(envelope.Payload)!;
        Assert.Equal("contact.email", payload.FieldCode);
        Assert.DoesNotContain("ref-prior-1", envelope.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("ada@example.com", envelope.Payload, StringComparison.OrdinalIgnoreCase);

        CustomerAuditEventRecord audit = await context.CustomerAuditEvents.SingleAsync(record =>
            record.Action == "customer.correction.recorded");
        Assert.Equal(customerId, audit.SubjectReference);
        Assert.DoesNotContain("ada@example.com", audit.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_verified_correction_invalidates_the_verification_that_satisfied_it()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await VerifiedCustomerAsync(context, "CORR-2");

        KycRequirementRecord saturated = await context.KycRequirements.SingleAsync(record => record.Status == "passed");
        Assert.NotNull(saturated.LinkedVerificationId);

        CustomerCorrectionView view = await ServiceAsync(context).RecordAsync(
            Correction(customerId, isVerifiedIdentifier: true, requirementCode: "contact_detail", approvalReference: "workflow-correction-1"),
            CancellationToken.None);

        Assert.True(view.RequiresReverification);

        context.ChangeTracker.Clear();
        KycRequirementRecord reopened = await context.KycRequirements.SingleAsync(record =>
            record.RequirementCode == "contact_detail");
        Assert.Equal("evidence_pending", reopened.Status);
        // The accepted verification stays as immutable evidence; it is simply no longer current.
        Assert.Single(await context.IdentityVerifications
            .Where(verification => verification.RequirementCode == "contact_detail")
            .ToListAsync());
    }

    [Fact]
    public async Task A_verified_correction_without_an_approval_is_refused()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await VerifiedCustomerAsync(context, "CORR-3");

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => ServiceAsync(context).RecordAsync(
                Correction(customerId, isVerifiedIdentifier: true, requirementCode: "contact_detail"),
                CancellationToken.None));

        Assert.Equal("customer_correction_approval_required", conflict.Code);
        Assert.Empty(await context.CustomerCorrections.ToListAsync());
    }

    [Fact]
    public async Task Corrections_are_listed_newest_first_and_scoped_to_the_tenant()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        string customerId = await CustomerAsync(context, "CORR-4");
        CustomerCorrectionService service = ServiceAsync(context);

        await service.RecordAsync(Correction(customerId), CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(5));
        await service.RecordAsync(
            Correction(customerId, fieldCode: "contact.phone", priorValueReference: "ref-prior-2", newValueReference: "ref-new-2"),
            CancellationToken.None);

        IReadOnlyList<CustomerCorrectionView> corrections = await service.ListAsync(
            new GetCustomerCorrectionsQuery(_institution, _steward, customerId), CancellationToken.None);

        Assert.Equal(2, corrections.Count);
        Assert.Equal("contact.phone", corrections[0].FieldCode);

        await using CustomerOnboardingDbContext otherContext = CreateContext(_otherInstitution);
        Assert.Empty(await ServiceAsync(otherContext).ListAsync(
            new GetCustomerCorrectionsQuery(_otherInstitution, _steward, customerId), CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_customer_is_refused()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);

        await Assert.ThrowsAsync<CustomerNotFoundException>(() =>
            ServiceAsync(context).RecordAsync(Correction(Guid.NewGuid().ToString("N")), CancellationToken.None));
    }

    private RecordCustomerCorrectionCommand Correction(
        string customerId,
        string fieldCode = "contact.email",
        string priorValueReference = "ref-prior-1",
        string newValueReference = "ref-new-1",
        bool isVerifiedIdentifier = false,
        string? requirementCode = null,
        string? approvalReference = null) =>
        new(
            _institution,
            _steward,
            customerId,
            fieldCode,
            priorValueReference,
            newValueReference,
            "customer supplied corrected details",
            EvidenceReference: "evidence-1",
            isVerifiedIdentifier,
            requirementCode,
            approvalReference,
            "correlation");

    private CustomerCorrectionService ServiceAsync(CustomerOnboardingDbContext context) =>
        new(context, new KycCaseService(context, _time), _time);

    private async Task<string> VerifiedCustomerAsync(CustomerOnboardingDbContext context, string reference)
    {
        (string customerId, string caseId, string verificationId) = await SetupAsync(context, reference);

        KycCaseService kyc = new(context, _time);
        await kyc.ApplyAttemptResultAsync(
            new ApplyVerificationAttemptResultCommand(
                _institution,
                _steward,
                verificationId,
                "attempt-1",
                VerificationAttemptStatus.Succeeded,
                CanonicalVerificationOutcome.Passed,
                "mapping-v1",
                "evidence-ref-1",
                new string('a', 64),
                "correlation"),
            CancellationToken.None);

        Assert.Equal(caseId, (await context.KycCases.SingleAsync()).CaseId);
        return customerId;
    }

    private async Task<string> CustomerAsync(CustomerOnboardingDbContext context, string reference)
    {
        (string customerId, _, _) = await SetupAsync(context, reference);
        return customerId;
    }

    private async Task<(string CustomerId, string CaseId, string VerificationId)> SetupAsync(
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

        CustomerService customers = new(context, _time);
        CustomerView customer = await customers.RegisterAsync(
            new RegisterCustomerCommand(_institution, _officer, submitted.ApplicationId, null, "correlation"),
            CancellationToken.None);

        KycCaseService kyc = new(context, _time);
        KycCaseView kycCase = await kyc.OpenAsync(
            new OpenKycCaseCommand(_institution, _steward, submitted.ApplicationId, submitted.Revision, "correlation"),
            CancellationToken.None);

        VerificationView verification = await kyc.RequestVerificationAsync(
            new RequestVerificationCommand(_institution, _steward, kycCase.CaseId, "contact_detail", "identity_document_check", "correlation"),
            CancellationToken.None);
        await kyc.RecordAttemptAsync(
            new RecordVerificationAttemptCommand(_institution, _steward, verification.VerificationId, "attempt-1", "provider-ref-1", null, "correlation"),
            CancellationToken.None);

        return (customer.CustomerId, kycCase.CaseId, verification.VerificationId);
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
