using System;
using System.Collections.Generic;
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
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Products;
using CoreBanking.CoreLedger.Infrastructure.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using CoreBanking.CoreLedger.Infrastructure.Products;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class AccountOpeningPolicyTests
{
    [Fact]
    public void Policy_round_trips_through_its_canonical_content()
    {
        AccountOpeningPolicy policy = AccountOpeningPolicy.Create(AccountLimitKey.ProductFamily, 3, 1);

        ConfigurationContent content = policy.ToConfigurationContent();

        Assert.Equal(policy, AccountOpeningPolicy.Parse(content.CanonicalJson));
        Assert.Equal(content.Hash, ConfigurationContent.Create(content.CanonicalJson).Hash);
    }

    [Theory]
    [InlineData("{\"limitKey\":\"product_family\",\"maximumOpenAccounts\":0,\"overrideAdditionalAccounts\":0}")]
    [InlineData("{\"limitKey\":\"product_family\",\"maximumOpenAccounts\":2,\"overrideAdditionalAccounts\":3}")]
    [InlineData("{\"limitKey\":\"other\",\"maximumOpenAccounts\":2,\"overrideAdditionalAccounts\":0}")]
    [InlineData("{\"limitKey\":\"product_family\",\"maximumOpenAccounts\":2,\"overrideAdditionalAccounts\":0,\"extra\":1}")]
    [InlineData("{\"limitKey\":\"product_family\",\"maximumOpenAccounts\":2}")]
    public void Policy_rejects_an_invalid_document(string json)
    {
        Assert.ThrowsAny<ArgumentException>(() => AccountOpeningPolicy.Parse(json));
    }

    [Fact]
    public void Policy_cannot_allow_an_override_larger_than_the_ceiling()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AccountOpeningPolicy.Create(AccountLimitKey.Product, 2, 3));
    }
}

public sealed class ExistingAccountsCriterionTests
{
    [Fact]
    public void Unrestricted_rules_keep_the_published_content_shape_and_hash_stable()
    {
        ProductEligibilityRules rules = Rules();

        ConfigurationContent content = rules.ToConfigurationContent();

        Assert.DoesNotContain("existingAccounts", content.CanonicalJson, StringComparison.Ordinal);
        Assert.Equal(rules.ToConfigurationContent().Hash, content.Hash);
    }

    [Fact]
    public void Restricted_rules_round_trip_the_limit_and_its_counting_basis()
    {
        ProductEligibilityRules rules = Rules(ExistingAccountsCriterion.Limit(2, AccountLimitKey.Currency));

        ConfigurationContent content = rules.ToConfigurationContent();
        ProductEligibilityRules restored = ProductEligibilityRules.FromConfigurationContent(content);

        Assert.False(restored.ExistingAccounts.AllowsAny);
        Assert.Equal(2, restored.ExistingAccounts.MaximumAccounts);
        Assert.Equal(AccountLimitKey.Currency, restored.ExistingAccounts.LimitKey);
        Assert.Equal(content.Hash, restored.ToConfigurationContent().Hash);
    }

    [Theory]
    [InlineData("{\"mode\":\"limit\"}")]
    [InlineData("{\"mode\":\"any\",\"maximumAccounts\":2}")]
    [InlineData("{\"mode\":\"limit\",\"maximumAccounts\":0,\"limitKey\":\"product\"}")]
    [InlineData("{\"mode\":\"limit\",\"maximumAccounts\":2,\"limitKey\":\"unknown\"}")]
    public void An_invalid_limit_is_refused(string existingAccountsJson)
    {
        string json = Rules().ToConfigurationContent().CanonicalJson.TrimEnd('}') + "," + existingAccountsJson[1..] + "}";

        Assert.ThrowsAny<ArgumentException>(() =>
            ProductEligibilityRules.FromConfigurationContent(ConfigurationContent.Create(json)));
    }

    internal static ProductEligibilityRules Rules(ExistingAccountsCriterion? existingAccounts = null) =>
        ProductEligibilityRules.Create(
            "savings-standard",
            1,
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityAgeCriterion.Between(18, 120),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Only(["NGN"]),
            existingAccounts);
}

public sealed class AccountEligibilityPolicyTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_missing_institution_ceiling_blocks_opening()
    {
        AccountEligibilityDecision decision = AccountEligibilityPolicy.Decide(
            ExistingAccountsCriterionTests.Rules(),
            Facts(),
            openingPolicy: null,
            Holdings(),
            "SAVINGS-001",
            "savings",
            "NGN",
            EvaluatedAt);

        Assert.False(decision.Eligible);
        Assert.Equal(AccountEligibilityPolicy.PolicyUnavailableCode, decision.Code);
        Assert.Null(decision.Limit);
    }

    [Fact]
    public void The_stricter_of_the_product_rule_and_the_ceiling_applies()
    {
        AccountEligibilityDecision decision = AccountEligibilityPolicy.Decide(
            ExistingAccountsCriterionTests.Rules(ExistingAccountsCriterion.Limit(5, AccountLimitKey.ProductFamily)),
            Facts(),
            AccountOpeningPolicy.Create(AccountLimitKey.ProductFamily, 3, 0),
            Holdings(),
            "SAVINGS-001",
            "savings",
            "NGN",
            EvaluatedAt);

        Assert.True(decision.Eligible);
        Assert.NotNull(decision.Limit);
        Assert.Equal(5, decision.Limit!.ProductMaximum);
        Assert.Equal(3, decision.Limit.EffectiveMaximum);
        Assert.Equal(0, decision.Limit.Counted);
    }

    [Fact]
    public void A_reached_limit_refuses_the_application_with_its_evidence()
    {
        AccountEligibilityDecision decision = AccountEligibilityPolicy.Decide(
            ExistingAccountsCriterionTests.Rules(ExistingAccountsCriterion.Limit(2, AccountLimitKey.ProductFamily)),
            Facts(),
            AccountOpeningPolicy.Create(AccountLimitKey.ProductFamily, 3, 0),
            Holdings("application-1", "application-2"),
            "SAVINGS-001",
            "savings",
            "NGN",
            EvaluatedAt);

        Assert.False(decision.Eligible);
        Assert.Equal(AccountEligibilityPolicy.LimitReachedCode, decision.Code);
        Assert.Equal(["application-1", "application-2"], decision.Limit!.CountedHoldingIds);
        Assert.Equal(2, decision.Limit.Counted);
        Assert.Equal(2, decision.Limit.EffectiveMaximum);
    }

    [Fact]
    public void Only_holdings_on_the_approved_counting_basis_are_counted()
    {
        AccountHolding[] holdings =
        [
            new("application-1", "Draft", "SAVINGS-001", "savings", "NGN"),
            new("application-2", "Approved", "SAVINGS-002", "savings", "USD"),
            new("application-3", "Ineligible", "SAVINGS-001", "savings", "NGN"),
        ];

        AccountEligibilityDecision byProduct = AccountEligibilityPolicy.Decide(
            ExistingAccountsCriterionTests.Rules(ExistingAccountsCriterion.Limit(2, AccountLimitKey.Product)),
            Facts(),
            AccountOpeningPolicy.Create(AccountLimitKey.Product, 2, 0),
            holdings,
            "SAVINGS-001",
            "savings",
            "NGN",
            EvaluatedAt);
        AccountEligibilityDecision byCurrency = AccountEligibilityPolicy.Decide(
            ExistingAccountsCriterionTests.Rules(ExistingAccountsCriterion.Limit(2, AccountLimitKey.Currency)),
            Facts(),
            AccountOpeningPolicy.Create(AccountLimitKey.Currency, 2, 0),
            holdings,
            "SAVINGS-001",
            "savings",
            "NGN",
            EvaluatedAt);

        Assert.Equal(["application-1"], byProduct.Limit!.CountedHoldingIds);
        Assert.True(byProduct.Eligible);
        Assert.Equal(["application-1"], byCurrency.Limit!.CountedHoldingIds);
        Assert.True(byCurrency.Eligible);
    }

    [Fact]
    public void A_product_rule_failure_is_reported_before_any_limit_assessment()
    {
        ProductEligibilityRules rules = ProductEligibilityRules.Create(
            "savings-standard",
            1,
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Only(["tier-1"]),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityAgeCriterion.Between(18, 120),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Only(["NGN"]),
            ExistingAccountsCriterion.Limit(2, AccountLimitKey.Product));

        AccountEligibilityDecision decision = AccountEligibilityPolicy.Decide(
            rules,
            Facts(kycTier: "tier-3"),
            AccountOpeningPolicy.Create(AccountLimitKey.Product, 3, 0),
            Holdings(),
            "SAVINGS-001",
            "savings",
            "NGN",
            EvaluatedAt);

        Assert.False(decision.Eligible);
        Assert.Equal("product_eligibility_failed", decision.Code);
        Assert.Null(decision.Limit);
    }

    internal static AuthoritativeProductEligibilityFacts Facts(string kycTier = "tier-1") =>
        new(
            "customer-facts-1",
            "branch-reference-1",
            "currency-reference-1",
            EvaluatedAt.AddSeconds(30),
            "Individual",
            kycTier,
            "Retail",
            "Low",
            34,
            "BR-001",
            ResidencyCountryCode: "NG",
            "NGN");

    internal static IReadOnlyCollection<AccountHolding> Holdings(params string[] settledApplicationIds) =>
        settledApplicationIds
            .Select(id => new AccountHolding(id, "Open", "SAVINGS-001", "savings", "NGN"))
            .ToArray();
}

public sealed class AccountApplicationLifecycleTests
{
    [Fact]
    public void Submission_and_evaluation_follow_the_approved_transitions()
    {
        AccountApplicationStatus pending = AccountApplicationLifecycle.BeginEligibilityEvaluation(
            AccountApplicationStatus.Draft);

        Assert.Equal(AccountApplicationStatus.EligibilityPending, pending);
        Assert.Equal(
            AccountApplicationStatus.Eligible,
            AccountApplicationLifecycle.RecordEligibility(pending, eligible: true));
        Assert.Equal(
            AccountApplicationStatus.Ineligible,
            AccountApplicationLifecycle.RecordEligibility(pending, eligible: false));
    }

    [Theory]
    [InlineData(AccountApplicationStatus.Eligible)]
    [InlineData(AccountApplicationStatus.Ineligible)]
    [InlineData(AccountApplicationStatus.Approved)]
    public void A_second_evaluation_is_refused(AccountApplicationStatus status)
    {
        Assert.Throws<InvalidOperationException>(() =>
            AccountApplicationLifecycle.BeginEligibilityEvaluation(status));
    }

    [Fact]
    public void Recording_a_decision_outside_evaluation_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AccountApplicationLifecycle.RecordEligibility(AccountApplicationStatus.Draft, eligible: true));
    }

    [Theory]
    [InlineData(AccountApplicationStatus.Draft, true)]
    [InlineData(AccountApplicationStatus.Approved, true)]
    [InlineData(AccountApplicationStatus.Open, true)]
    [InlineData(AccountApplicationStatus.Ineligible, false)]
    [InlineData(AccountApplicationStatus.Rejected, false)]
    [InlineData(AccountApplicationStatus.CancelledBeforeActivation, false)]
    public void The_counted_status_set_matches_the_approved_rule(AccountApplicationStatus status, bool counted)
    {
        Assert.Equal(counted, AccountApplicationLifecycle.CountsTowardsLimit(status));
    }
}

public sealed class AccountContractsAndPermissionsTests
{
    [Fact]
    public void The_application_request_carries_no_trusted_field()
    {
        string[] properties = typeof(CreateAccountApplicationRequestV1)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("InstitutionId", properties);
        Assert.DoesNotContain("Status", properties);
        Assert.DoesNotContain("ApplicationId", properties);
        Assert.DoesNotContain("ProductVersionId", properties);
        Assert.DoesNotContain("EligibilitySnapshotId", properties);
    }

    [Fact]
    public void Account_permissions_follow_the_three_segment_grammar()
    {
        Assert.Equal("account.application.create", AccountPermissions.ApplicationCreate.Value);
        Assert.Equal("account.application.view", AccountPermissions.ApplicationView.Value);
        Assert.Equal("account", AccountPermissions.ApplicationCreate.Domain);
        Assert.Equal("application", AccountPermissions.ApplicationCreate.Resource);
        Assert.True(AccountPermissions.ApplicationCreateRequirement.RequiresStepUp);
    }

    [Fact]
    public void The_evaluation_evidence_round_trips()
    {
        AccountEligibilityEvaluationResponseV1 response = new(
            "institution-1",
            "application-1",
            "evaluation-1",
            "version-1",
            new string('a', 64),
            "customer-facts-1",
            "branch-reference-1",
            "currency-reference-1",
            true,
            true,
            "eligible",
            "product_family",
            1,
            3,
            ["application-0"],
            [new EligibilityRuleTraceV1("kyc_tier", "tier-1", true)],
            "Eligible",
            2,
            new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero),
            false);

        AccountEligibilityEvaluationResponseV1 restored =
            CoreBanking.BuildingBlocks.Application.Messaging.EventContract
                .Deserialize<AccountEligibilityEvaluationResponseV1>(
                    CoreBanking.BuildingBlocks.Application.Messaging.EventContract.Serialize(response))!;

        Assert.Equal(response.InstitutionId, restored.InstitutionId);
        Assert.Equal(response.ApplicationId, restored.ApplicationId);
        Assert.Equal(response.EvaluationId, restored.EvaluationId);
        Assert.Equal(response.ProductContentHash, restored.ProductContentHash);
        Assert.Equal(response.DecisionCode, restored.DecisionCode);
        Assert.Equal(response.LimitKey, restored.LimitKey);
        Assert.Equal(response.CountedHoldings, restored.CountedHoldings);
        Assert.Equal(response.EffectiveMaximum, restored.EffectiveMaximum);
        Assert.Equal(response.CountedHoldingIds, restored.CountedHoldingIds);
        Assert.Equal(response.Trace, restored.Trace);
        Assert.Equal(response.ApplicationStatus, restored.ApplicationStatus);
        Assert.Equal(response.ApplicationVersion, restored.ApplicationVersion);
        Assert.Equal(response.EvaluatedAt, restored.EvaluatedAt);
        Assert.Equal(response.Eligible, restored.Eligible);
    }
}

public sealed class AccountLimitBasisTests
{
    [Fact]
    public void An_unrestricted_product_rule_uses_the_institution_counting_basis()
    {
        AccountLimitBasis basis = AccountEligibilityPolicy.ResolveLimitBasis(
            ExistingAccountsCriterionTests.Rules(),
            AccountOpeningPolicy.Create(AccountLimitKey.ProductFamily, 3, 0),
            "SAVINGS-001",
            "savings",
            "NGN");

        Assert.Equal(AccountLimitKey.ProductFamily, basis.LimitKey);
        Assert.Equal("savings", basis.LimitKeyValue);
        Assert.Null(basis.ProductMaximum);
        Assert.Equal(3, basis.EffectiveMaximum);
    }

    [Theory]
    [InlineData(AccountLimitKey.Product, "SAVINGS-001")]
    [InlineData(AccountLimitKey.Currency, "NGN")]
    [InlineData(AccountLimitKey.Institution, "*")]
    public void A_restricted_product_rule_uses_its_own_basis_and_the_stricter_maximum(
        AccountLimitKey limitKey,
        string expectedValue)
    {
        AccountLimitBasis basis = AccountEligibilityPolicy.ResolveLimitBasis(
            ExistingAccountsCriterionTests.Rules(ExistingAccountsCriterion.Limit(2, limitKey)),
            AccountOpeningPolicy.Create(AccountLimitKey.Institution, 5, 0),
            "SAVINGS-001",
            "savings",
            "NGN");

        Assert.Equal(limitKey, basis.LimitKey);
        Assert.Equal(expectedValue, basis.LimitKeyValue);
        Assert.Equal(2, basis.ProductMaximum);
        Assert.Equal(5, basis.InstitutionMaximum);
        Assert.Equal(2, basis.EffectiveMaximum);
    }
}

public sealed class AccountPersistenceModelTests
{
    [Fact]
    public void One_unreleased_claim_per_customer_and_basis_is_enforced_by_a_unique_filtered_index()
    {
        using CoreLedgerDbContext context = CreateContext();
        var claim = context.Model.FindEntityType(typeof(AccountOpeningClaimRecord))
            ?? throw new InvalidOperationException("Account opening claim mapping is missing.");

        Assert.Contains(claim.GetIndexes(), index =>
            index.IsUnique
            && index.GetFilter() == "released_at IS NULL"
            && index.Properties.Select(property => property.Name).SequenceEqual(
                ["InstitutionId", "CustomerId", "LimitKey", "LimitKeyValue"]));
    }

    [Fact]
    public void Account_application_and_evaluation_are_tenant_owned_and_fail_closed()
    {
        using CoreLedgerDbContext context = CreateContext();

        Assert.NotEmpty(context.Model.FindEntityType(typeof(AccountApplicationRecord))!.GetDeclaredQueryFilters());
        Assert.NotEmpty(context.Model.FindEntityType(typeof(AccountEligibilityEvaluationRecord))!.GetDeclaredQueryFilters());
        Assert.NotEmpty(context.Model.FindEntityType(typeof(AccountOpeningClaimRecord))!.GetDeclaredQueryFilters());
    }

    private static CoreLedgerDbContext CreateContext()
    {
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(InstitutionId.From("institution-trusted"));
        DbContextOptions<CoreLedgerDbContext> options = new DbContextOptionsBuilder<CoreLedgerDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;******")
            .Options;
        return new CoreLedgerDbContext(options, tenantContext);
    }
}

public sealed class AccountApplicationServiceTests
{
    [Fact]
    public async Task An_application_cannot_be_created_for_another_institution()
    {
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(InstitutionId.From("institution-trusted"));
        await using CoreLedgerDbContext context = CreateContext(tenantContext);
        AccountApplicationService service = new(
            context,
            new IdempotencyService(context, TimeProvider.System),
            tenantContext,
            TimeProvider.System,
            new AuthoritativeEligibilityFactsResolver(
                new UnconfiguredCustomerEligibilityFactsSource(),
                new UnavailableBranchSource(),
                new UnavailableCurrencySource()),
            UnconfiguredAccountOpeningPolicySource.Instance,
            new AccountHoldingsReader(context));

        AccountApplicationRejectedException exception = await Assert.ThrowsAsync<AccountApplicationRejectedException>(
            () => service.CreateAsync(
                new CreateAccountApplicationCommand(
                    InstitutionId.From("institution-other"),
                    ActorId.From("actor-1"),
                    "idempotency-1",
                    "application-1",
                    "request-1",
                    "customer-1",
                    "product-1",
                    "branch-1",
                    "NGN",
                    "SAVINGS",
                    "Individual",
                    "correlation-1"),
                CancellationToken.None));

        Assert.Equal("cross_tenant_access_denied", exception.Code);
    }

    [Fact]
    public async Task Eligibility_evidence_cannot_be_read_for_another_institution()
    {
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(InstitutionId.From("institution-trusted"));
        await using CoreLedgerDbContext context = CreateContext(tenantContext);
        AccountApplicationService service = new(
            context,
            new IdempotencyService(context, TimeProvider.System),
            tenantContext,
            TimeProvider.System,
            new AuthoritativeEligibilityFactsResolver(
                new UnconfiguredCustomerEligibilityFactsSource(),
                new UnavailableBranchSource(),
                new UnavailableCurrencySource()),
            UnconfiguredAccountOpeningPolicySource.Instance,
            new AccountHoldingsReader(context));

        AccountApplicationRejectedException exception = await Assert.ThrowsAsync<AccountApplicationRejectedException>(
            () => service.GetLatestEvaluationAsync(
                new GetAccountApplicationQuery(InstitutionId.From("institution-other"), "application-1"),
                CancellationToken.None));

        Assert.Equal("cross_tenant_access_denied", exception.Code);
    }

    private static CoreLedgerDbContext CreateContext(AmbientTenantContext tenantContext)
    {
        DbContextOptions<CoreLedgerDbContext> options = new DbContextOptionsBuilder<CoreLedgerDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;******")
            .Options;
        return new CoreLedgerDbContext(options, tenantContext);
    }

    private sealed class UnavailableBranchSource : IBranchReferenceSource
    {
        public Task<BranchReferenceSnapshot?> FindAsync(
            InstitutionId institutionId,
            string branchId,
            CancellationToken cancellationToken) => Task.FromResult<BranchReferenceSnapshot?>(null);
    }

    private sealed class UnavailableCurrencySource : ICurrencyReferenceSource
    {
        public Task<CurrencyReferenceSnapshot?> GetAsync(
            InstitutionId institutionId,
            DateTimeOffset asOf,
            CancellationToken cancellationToken) => Task.FromResult<CurrencyReferenceSnapshot?>(null);
    }
}
