using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Api.Authorisation;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Domain.Products;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

/// <summary>
/// Reference facts may only come from their owners, may never be replaced by a last-known value, and
/// must fail closed with a stable code. Decision: P1-E04-D17.
/// </summary>
public sealed class ReferenceFactTransportTests
{
    private static readonly InstitutionId Institution = InstitutionId.From("institution-1");
    private static readonly DateTimeOffset AsOf = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedCurrencies = ["NGN", "USD"];

    [Fact]
    public async Task Current_facts_from_each_owner_are_assembled_with_their_versions()
    {
        ResolvedEligibilityFacts result = await NewResolver().ResolveAsync(
            Institution, "customer-1", "branch-1", "ngn", AsOf, CancellationToken.None);

        Assert.Null(result.RejectionCode);
        Assert.NotNull(result.Facts);
        Assert.Equal("c7", result.Facts!.CustomerFactsVersion);
        Assert.Equal("b3.i9", result.Facts.BranchReferenceVersion);
        Assert.Equal("currency-v4", result.Facts.CurrencyReferenceVersion);
        Assert.Equal("LAGOS-001", result.Facts.BranchCode);
        Assert.Equal("NGN", result.Facts.CurrencyCode);
        Assert.Equal("KYC-2", result.Facts.KycTier);
        Assert.Equal(30, result.Facts.AgeYears);
        Assert.Null(result.Facts.ResidencyCountryCode);
    }

    [Fact]
    public async Task Facts_past_their_declared_validity_fail_closed_as_stale()
    {
        ResolvedEligibilityFacts result = await Build(
                Snapshot(validUntil: AsOf.AddSeconds(-1)),
                null,
                DefaultBranch(),
                DefaultCurrency())
            .ResolveAsync(Institution, "customer-1", "branch-1", "NGN", AsOf, CancellationToken.None);

        Assert.Equal("eligibility_facts_stale", result.RejectionCode);
        Assert.Null(result.Facts);
    }

    [Fact]
    public async Task A_window_longer_than_the_approved_ceiling_is_refused()
    {
        ResolvedEligibilityFacts result = await Build(
                Snapshot(validUntil: AsOf.AddSeconds(61), asOf: AsOf),
                null,
                DefaultBranch(),
                DefaultCurrency())
            .ResolveAsync(Institution, "customer-1", "branch-1", "NGN", AsOf, CancellationToken.None);

        Assert.Equal("eligibility_facts_window_exceeded", result.RejectionCode);
    }

    [Fact]
    public async Task Facts_for_another_institution_are_refused_as_a_mismatch()
    {
        ResolvedEligibilityFacts result = await Build(
                Snapshot(institutionId: "institution-2"),
                null,
                DefaultBranch(),
                DefaultCurrency())
            .ResolveAsync(Institution, "customer-1", "branch-1", "NGN", AsOf, CancellationToken.None);

        Assert.Equal("eligibility_facts_mismatch", result.RejectionCode);
    }

    [Fact]
    public async Task An_inactive_branch_is_not_available()
    {
        ResolvedEligibilityFacts result = await Build(
                Snapshot(),
                null,
                new BranchReferenceSnapshot("branch-1", "LAGOS-001", "suspended", "b3.i9"),
                DefaultCurrency())
            .ResolveAsync(Institution, "customer-1", "branch-1", "NGN", AsOf, CancellationToken.None);

        Assert.Equal("branch_not_available", result.RejectionCode);
    }

    [Fact]
    public async Task A_missing_currency_reference_is_not_available()
    {
        ResolvedEligibilityFacts result = await Build(Snapshot(), null, DefaultBranch(), null)
            .ResolveAsync(Institution, "customer-1", "branch-1", "NGN", AsOf, CancellationToken.None);

        Assert.Equal("currency_reference_unavailable", result.RejectionCode);
    }

    [Fact]
    public async Task A_currency_outside_the_supported_set_is_refused()
    {
        ResolvedEligibilityFacts result = await NewResolver()
            .ResolveAsync(Institution, "customer-1", "branch-1", "USD", AsOf, CancellationToken.None);

        Assert.Equal("currency_not_supported", result.RejectionCode);
    }

    [Fact]
    public async Task An_owner_rejection_keeps_its_stable_code()
    {
        ResolvedEligibilityFacts result = await Build(
                null,
                "customer_classification_unavailable",
                DefaultBranch(),
                DefaultCurrency())
            .ResolveAsync(Institution, "customer-1", "branch-1", "NGN", AsOf, CancellationToken.None);

        Assert.Equal("customer_classification_unavailable", result.RejectionCode);
    }

    [Fact]
    public void The_currency_set_requires_the_base_currency_to_be_a_member()
    {
        Assert.Throws<ArgumentException>(() => InstitutionCurrencySet.Parse(
            "{\"baseCurrency\":\"NGN\",\"supportedCurrencies\":[\"USD\"]}"));
    }

    [Fact]
    public void The_currency_set_rejects_unknown_properties_and_duplicates()
    {
        Assert.Throws<ArgumentException>(() => InstitutionCurrencySet.Parse(
            "{\"baseCurrency\":\"NGN\",\"supportedCurrencies\":[\"NGN\"],\"fxRates\":[]}"));
        Assert.Throws<ArgumentException>(() => InstitutionCurrencySet.Parse(
            "{\"baseCurrency\":\"NGN\",\"supportedCurrencies\":[\"NGN\",\"ngn\"]}"));
    }

    [Fact]
    public void The_currency_set_normalises_codes_and_reports_membership()
    {
        InstitutionCurrencySet set = InstitutionCurrencySet.Parse(
            "{\"baseCurrency\":\"ngn\",\"supportedCurrencies\":[\"usd\",\"NGN\"]}");

        Assert.Equal("NGN", set.BaseCurrency);
        Assert.Equal(ExpectedCurrencies, set.SupportedCurrencies);
        Assert.True(set.Supports("ngn"));
        Assert.False(set.Supports("EUR"));
    }

    [Fact]
    public void A_rule_that_needs_residency_cannot_be_evaluated_without_the_fact()
    {
        ProductEligibilityRules rules = ProductEligibilityRules.Create(
            "savings-eligibility",
            1,
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityAgeCriterion.Any(),
            EligibilityCodeCriterion.Any(),
            EligibilityCodeCriterion.Only(["NG"]),
            EligibilityCodeCriterion.Any());

        FinalEligibilityDecision decision = ProductEligibilityApprovalPolicy.RecheckForFinalApproval(
            rules,
            Facts(residencyCountryCode: null),
            AsOf);

        Assert.False(decision.Eligible);
        Assert.Equal("residency_fact_unavailable", decision.Code);
    }

    [Fact]
    public async Task A_conflict_from_the_owner_keeps_its_bounded_code()
    {
        RemoteCustomerEligibilityFactsSource source = new(
            new HttpClient(new StubHandler(HttpStatusCode.Conflict, "{\"code\":\"customer_classification_unavailable\"}"))
            {
                BaseAddress = new Uri("http://customer.test"),
            });

        CustomerFactsLookup lookup = await source.GetAsync(Institution, "customer-1", AsOf, CancellationToken.None);

        Assert.Null(lookup.Snapshot);
        Assert.Equal("customer_classification_unavailable", lookup.RejectionCode);
    }

    [Fact]
    public async Task A_malformed_answer_is_refused_rather_than_guessed()
    {
        RemoteCustomerEligibilityFactsSource source = new(
            new HttpClient(new StubHandler(HttpStatusCode.OK, "{\"schemaVersion\":1,\"customerId\":\"customer-1\"}"))
            {
                BaseAddress = new Uri("http://customer.test"),
            });

        CustomerFactsLookup lookup = await source.GetAsync(Institution, "customer-1", AsOf, CancellationToken.None);

        Assert.Equal("eligibility_facts_malformed", lookup.RejectionCode);
    }

    [Fact]
    public async Task A_found_answer_is_projected_without_personal_data()
    {
        const string Body = """
            {
              "schemaVersion": 1,
              "institutionId": "institution-1",
              "customerId": "customer-1",
              "customerStatus": "active",
              "effectiveClassificationVersion": 4,
              "kycTier": "KYC-2",
              "segment": "RETAIL",
              "riskClass": "LOW",
              "requiresEnhancedDueDiligence": false,
              "customerType": "individual",
              "ageYears": 31,
              "factsVersion": "c7",
              "asOf": "2026-11-06T12:00:00+00:00",
              "validUntil": "2026-11-06T12:01:00+00:00"
            }
            """;

        RemoteCustomerEligibilityFactsSource source = new(
            new HttpClient(new StubHandler(HttpStatusCode.OK, Body))
            {
                BaseAddress = new Uri("http://customer.test"),
            });

        CustomerFactsLookup lookup = await source.GetAsync(Institution, "customer-1", AsOf, CancellationToken.None);

        Assert.Null(lookup.RejectionCode);
        Assert.NotNull(lookup.Snapshot);
        Assert.Equal("c7", lookup.Snapshot!.FactsVersion);
        Assert.Equal("KYC-2", lookup.Snapshot.KycTier);
        Assert.Equal(31, lookup.Snapshot.AgeYears);
    }

    private static AuthoritativeEligibilityFactsResolver NewResolver() =>
        Build(Snapshot(), null, DefaultBranch(), DefaultCurrency());

    private static AuthoritativeEligibilityFactsResolver Build(
        CustomerEligibilitySnapshot? customer,
        string? customerRejection,
        BranchReferenceSnapshot? branch,
        CurrencyReferenceSnapshot? currency) =>
        new(
            new StubCustomerSource(customer, customerRejection),
            new StubBranchSource(branch),
            new StubCurrencySource(currency));

    private static BranchReferenceSnapshot DefaultBranch() =>
        new("branch-1", "LAGOS-001", "active", "b3.i9");

    private static CurrencyReferenceSnapshot DefaultCurrency() =>
        new(Institution.Value, ["NGN"], "currency-v4");

    private static CustomerEligibilitySnapshot Snapshot(
        string institutionId = "institution-1",
        DateTimeOffset? validUntil = null,
        DateTimeOffset? asOf = null)
    {
        DateTimeOffset basis = asOf ?? AsOf;
        return new CustomerEligibilitySnapshot(
            institutionId,
            "customer-1",
            "active",
            "KYC-2",
            "RETAIL",
            "LOW",
            false,
            "individual",
            30,
            "c7",
            basis,
            validUntil ?? basis.AddSeconds(60));
    }

    private static AuthoritativeProductEligibilityFacts Facts(string? residencyCountryCode) => new(
        "c7",
        "b3.i9",
        "currency-v4",
        AsOf.AddSeconds(60),
        "individual",
        "KYC-2",
        "RETAIL",
        "LOW",
        30,
        "LAGOS-001",
        residencyCountryCode,
        "NGN");

    private sealed class StubCustomerSource(CustomerEligibilitySnapshot? snapshot, string? rejection)
        : ICustomerEligibilityFactsSource
    {
        public Task<CustomerFactsLookup> GetAsync(
            InstitutionId institutionId,
            string customerId,
            DateTimeOffset asOf,
            CancellationToken cancellationToken) =>
            Task.FromResult(snapshot is null
                ? CustomerFactsLookup.Rejected(rejection ?? "eligibility_facts_unavailable")
                : CustomerFactsLookup.Found(snapshot));
    }

    private sealed class StubBranchSource(BranchReferenceSnapshot? snapshot) : IBranchReferenceSource
    {
        public Task<BranchReferenceSnapshot?> FindAsync(
            InstitutionId institutionId,
            string branchId,
            CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }

    private sealed class StubCurrencySource(CurrencyReferenceSnapshot? snapshot) : ICurrencyReferenceSource
    {
        public Task<CurrencyReferenceSnapshot?> GetAsync(
            InstitutionId institutionId,
            DateTimeOffset asOf,
            CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
