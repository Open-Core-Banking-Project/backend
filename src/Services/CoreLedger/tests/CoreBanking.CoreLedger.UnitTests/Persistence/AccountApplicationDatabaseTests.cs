using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests.Persistence;

/// <summary>
/// The account-application boundary against a real <c>core_ledger_db</c>, as the application principal.
/// Proves eligibility evidence, approval binding and tenant isolation over stored rows rather than over
/// the model. Excluded from the default run and executed by <c>scripts/db-verify.ps1</c>.
/// Decisions: P1-E04-D07, P1-E04-D09, P1-E04-D17.
/// </summary>
[Trait("Category", "Database")]
public sealed class AccountApplicationDatabaseTests : IDisposable
{
    private static readonly ActorId Maker = ActorId.From("db-maker");

    private readonly CoreLedgerDatabaseScope _scope = new();
    private readonly FixedTimeProvider _time;

    public AccountApplicationDatabaseTests() => _time = new FixedTimeProvider(CoreLedgerDatabaseScope.SeededAt);

    public void Dispose() => _scope.Dispose();

    [Fact]
    public async Task An_application_is_evaluated_against_stored_facts_and_keeps_its_evidence()
    {
        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountApplicationService service = CreateApplicationService(context, tenantContext);

        AccountApplicationCreatedResult created = await service.CreateAsync(
            CreateCommand("create-1", "application-1", "customer-1"),
            CancellationToken.None);

        Assert.Equal(AccountApplicationStatus.Draft, created.Status);
        Assert.Equal(_scope.ProductVersionId, created.ProductVersionId);
        Assert.Equal(_scope.ProductCode, created.ProductCode);

        AccountEligibilityResult evaluated = await service.EvaluateAsync(
            new EvaluateAccountEligibilityCommand(
                _scope.Institution,
                Maker,
                "evaluate-1",
                "application-1",
                created.Version,
                "customer_request",
                "correlation-2"),
            CancellationToken.None);

        Assert.True(evaluated.Eligible);
        Assert.True(evaluated.FactsCurrent);
        Assert.Equal(AccountApplicationStatus.Eligible, evaluated.Status);
        Assert.Equal(created.Version + 1, evaluated.Version);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        AccountApplicationRecord application = await verification.AccountApplications
            .SingleAsync(record => record.AccountApplicationId == "application-1");
        Assert.Equal("Eligible", application.Status);
        Assert.Equal(2L, application.Version);

        AccountEligibilityEvaluationRecord evidence = await verification.AccountEligibilityEvaluations.SingleAsync();
        Assert.True(evidence.Eligible);
        Assert.True(evidence.FactsCurrent);
        Assert.Equal(evaluated.EvaluationId, evidence.EvaluationId);
        Assert.Equal(_scope.ProductVersionId, evidence.ProductVersionId);
        Assert.Equal(FixedCustomerEligibilityFactsSource.FactsVersion, evidence.CustomerFactsVersion);
        Assert.Equal(FixedBranchReferenceSource.ReferenceVersion, evidence.BranchReferenceVersion);
        Assert.Equal(FixedCurrencyReferenceSource.ReferenceVersion, evidence.CurrencyReferenceVersion);

        InstitutionAuditRecord[] audit = await verification.InstitutionAuditEvents.ToArrayAsync();
        Assert.Contains(audit, record => record.Action == "account.application.create");
        Assert.Contains(audit, record => record.Action == "account.application.evaluate");
    }

    [Fact]
    public async Task A_replayed_create_returns_the_recorded_result_without_a_second_application()
    {
        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountApplicationService service = CreateApplicationService(context, tenantContext);
        CreateAccountApplicationCommand command = CreateCommand("create-2", "application-2", "customer-2");

        AccountApplicationCreatedResult first = await service.CreateAsync(command, CancellationToken.None);
        AccountApplicationCreatedResult replayed = await service.CreateAsync(command, CancellationToken.None);

        Assert.False(first.Replayed);
        Assert.True(replayed.Replayed);
        Assert.Equal(first.ApplicationId, replayed.ApplicationId);
        Assert.Equal(first.ProductVersionId, replayed.ProductVersionId);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        Assert.Equal(1, await verification.AccountApplications.CountAsync());
    }

    [Fact]
    public async Task Submitting_an_eligible_application_pins_the_version_and_snapshot_hash()
    {
        string applicationId = "application-3";
        long version = await CreateEligibleApplicationAsync(applicationId, "customer-3", "submit");

        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountApprovalService service = new AccountApprovalService(
            context,
            new IdempotencyService(context, _time),
            tenantContext,
            _time);

        SubmitAccountApplicationForApprovalResult submitted = await service.SubmitForApprovalAsync(
            new SubmitAccountApplicationForApprovalCommand(
                _scope.Institution,
                Maker,
                "approval-submit-1",
                applicationId,
                version,
                "request-3",
                "source-key-3",
                "correlation-3"),
            CancellationToken.None);

        Assert.Equal(nameof(AccountApplicationStatus.PendingApproval), submitted.Status);
        Assert.Equal(64, submitted.EligibilitySnapshotHash.Length);
        Assert.Equal(version + 1, submitted.Version);

        await using CoreLedgerDbContext verification = _scope.CreateContext();
        AccountApplicationRecord application = await verification.AccountApplications
            .SingleAsync(record => record.AccountApplicationId == applicationId);
        Assert.Equal("PendingApproval", application.Status);
        Assert.Equal("request-3", application.ApprovalRequestId);
        Assert.Equal(submitted.EligibilityEvaluationId, application.EligibilityEvaluationId);
        Assert.Equal(submitted.EligibilitySnapshotHash, application.EligibilitySnapshotHash);

        OutboxRecord[] outbox = await verification.OutboxMessages.ToArrayAsync();
        OutboxRecord requested = Assert.Single(outbox, record => record.EventType == "approval_requested");
        Assert.Equal("request-3", requested.BusinessReference);
        Assert.Contains(submitted.EligibilitySnapshotHash, requested.Envelope, StringComparison.Ordinal);
        Assert.Contains(AccountOpeningApprovalV1.WorkflowVersionId, requested.Envelope, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_institution_cannot_read_the_application_or_its_evaluation()
    {
        string applicationId = "application-4";
        await CreateEligibleApplicationAsync(applicationId, "customer-4", "isolation");

        InstitutionId foreignInstitution = InstitutionId.From("ledger-db-foreign-" + Guid.NewGuid().ToString("N")[..8]);
        AmbientTenantContext foreignTenant = new();
        foreignTenant.Establish(foreignInstitution);
        await using CoreLedgerDbContext foreignContext = _scope.CreateContext(foreignTenant);
        AccountApplicationService foreignService = CreateApplicationService(foreignContext, foreignTenant);

        AccountEligibilityResult? visible = await foreignService.GetLatestEvaluationAsync(
            new GetAccountApplicationQuery(foreignInstitution, applicationId),
            CancellationToken.None);
        Assert.Null(visible);

        AccountApplicationRejectedException refused = await Assert.ThrowsAsync<AccountApplicationRejectedException>(() =>
            foreignService.EvaluateAsync(
                new EvaluateAccountEligibilityCommand(
                    foreignInstitution,
                    Maker,
                    "evaluate-foreign-1",
                    applicationId,
                    2,
                    "customer_request",
                    "correlation-foreign-1"),
                CancellationToken.None));
        Assert.Equal("account_application_not_found", refused.Code);
    }

    private async Task<long> CreateEligibleApplicationAsync(string applicationId, string customerId, string keyPrefix)
    {
        AmbientTenantContext tenantContext = _scope.NewTenant();
        await using CoreLedgerDbContext context = _scope.CreateContext(tenantContext);
        AccountApplicationService service = CreateApplicationService(context, tenantContext);

        AccountApplicationCreatedResult created = await service.CreateAsync(
            CreateCommand(keyPrefix + "-create", applicationId, customerId),
            CancellationToken.None);
        AccountEligibilityResult evaluated = await service.EvaluateAsync(
            new EvaluateAccountEligibilityCommand(
                _scope.Institution,
                Maker,
                keyPrefix + "-evaluate",
                applicationId,
                created.Version,
                "customer_request",
                keyPrefix + "-correlation"),
            CancellationToken.None);
        Assert.True(evaluated.Eligible);
        return evaluated.Version;
    }

    private CreateAccountApplicationCommand CreateCommand(string idempotencyKey, string applicationId, string customerId) =>
        new(
            _scope.Institution,
            Maker,
            idempotencyKey,
            applicationId,
            applicationId + "-request",
            customerId,
            _scope.ProductId,
            _scope.BranchId,
            "NGN",
            "SAVINGS",
            "Individual",
            idempotencyKey + "-correlation");

    private AccountApplicationService CreateApplicationService(
        CoreLedgerDbContext context,
        AmbientTenantContext tenantContext) =>
        new AccountApplicationService(
            context,
            new IdempotencyService(context, _time),
            tenantContext,
            _time,
            new AuthoritativeEligibilityFactsResolver(
                new FixedCustomerEligibilityFactsSource(),
                new FixedBranchReferenceSource(),
                new FixedCurrencyReferenceSource()),
            new FixedAccountOpeningPolicySource(),
            new AccountHoldingsReader(context));
}
