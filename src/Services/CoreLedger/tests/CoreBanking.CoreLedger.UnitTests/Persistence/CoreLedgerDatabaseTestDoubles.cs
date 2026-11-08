using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Domain.Accounts;

namespace CoreBanking.CoreLedger.UnitTests.Persistence;

/// <summary>
/// A clock pinned to one instant, so every timestamp a database suite records is reproducible.
/// </summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    internal FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>The approved institution ceiling the eligibility decision must be evaluated against.</summary>
internal sealed class FixedAccountOpeningPolicySource : IAccountOpeningPolicySource
{
    private static readonly AccountOpeningPolicy Policy = AccountOpeningPolicy.Create(AccountLimitKey.Product, 3, 0);

    public Task<AccountOpeningPolicyLookup> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        Task.FromResult(AccountOpeningPolicyLookup.Found(Policy, "policy-version-1"));
}

/// <summary>
/// Authoritative customer facts inside the approved 60-second freshness ceiling, owned by the customer
/// service in production and by this double in the database suite.
/// </summary>
internal sealed class FixedCustomerEligibilityFactsSource : ICustomerEligibilityFactsSource
{
    internal const string FactsVersion = "customer-facts-1";
    internal const string KycTier = "tier-1";

    public Task<CustomerFactsLookup> GetAsync(
        InstitutionId institutionId,
        string customerId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        Task.FromResult(CustomerFactsLookup.Found(new CustomerEligibilitySnapshot(
            institutionId.Value,
            customerId,
            "active",
            KycTier,
            "Retail",
            "Low",
            RequiresEnhancedDueDiligence: false,
            "Individual",
            34,
            FactsVersion,
            asOf,
            asOf.AddSeconds(30))));
}

internal sealed class FixedBranchReferenceSource : IBranchReferenceSource
{
    internal const string ReferenceVersion = "branch-reference-1";

    public Task<BranchReferenceSnapshot?> FindAsync(
        InstitutionId institutionId,
        string branchId,
        CancellationToken cancellationToken) =>
        Task.FromResult<BranchReferenceSnapshot?>(
            new BranchReferenceSnapshot(branchId, "BR000001", "active", ReferenceVersion));
}

internal sealed class FixedCurrencyReferenceSource : ICurrencyReferenceSource
{
    internal const string ReferenceVersion = "currency-reference-1";

    public Task<CurrencyReferenceSnapshot?> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        Task.FromResult<CurrencyReferenceSnapshot?>(
            new CurrencyReferenceSnapshot(institutionId.Value, ["NGN"], ReferenceVersion));
}

/// <summary>The published account-number scheme; allocation itself always runs against the database.</summary>
internal sealed class FixedAccountNumberSchemeSource : IAccountNumberSchemeSource
{
    internal const string SchemeVersionId = "db-scheme-version-1";

    private static readonly AccountNumberScheme Scheme =
        AccountNumberScheme.Create("db-scheme", 1, "00", 0, 0, 6, AccountNumberCheckDigit.None);

    public Task<AccountNumberSchemeLookup> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        Task.FromResult(AccountNumberSchemeLookup.Found(Scheme, SchemeVersionId));
}
