using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Products;

namespace CoreBanking.CoreLedger.Application.Products;

/// <summary>Customer facts supplied by their owner. Decision: P1-E04-D17.</summary>
public sealed record CustomerEligibilitySnapshot(
    string InstitutionId,
    string CustomerId,
    string CustomerStatus,
    string KycTier,
    string Segment,
    string RiskClass,
    bool RequiresEnhancedDueDiligence,
    string CustomerType,
    int AgeYears,
    string FactsVersion,
    DateTimeOffset AsOf,
    DateTimeOffset ValidUntil);

/// <summary>The lookup result, including the stable code when the owner cannot answer.</summary>
public sealed record CustomerFactsLookup(CustomerEligibilitySnapshot? Snapshot, string? RejectionCode)
{
    public static CustomerFactsLookup Found(CustomerEligibilitySnapshot snapshot) => new(snapshot, null);

    public static CustomerFactsLookup Rejected(string code) => new(null, code);
}

/// <summary>Branch reference owned by the institution module of this service.</summary>
public sealed record BranchReferenceSnapshot(string BranchId, string BranchCode, string Status, string ReferenceVersion);

/// <summary>Currency reference owned by the institution module of this service.</summary>
public sealed record CurrencyReferenceSnapshot(
    string InstitutionId,
    IReadOnlyCollection<string> SupportedCurrencies,
    string ReferenceVersion);

/// <summary>Bounded reference-fact outcome telemetry. Implementations must never record payload values.</summary>
public interface IReferenceFactTelemetry
{
    void Record(string outcome);
}

/// <summary>Telemetry sink used when no meter is configured (tests and simulations).</summary>
public sealed class NullReferenceFactTelemetry : IReferenceFactTelemetry
{
    public static NullReferenceFactTelemetry Instance { get; } = new();

    public void Record(string outcome)
    {
    }
}

public interface ICustomerEligibilityFactsSource
{
    Task<CustomerFactsLookup> GetAsync(
        InstitutionId institutionId,
        string customerId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}

public interface IBranchReferenceSource
{
    Task<BranchReferenceSnapshot?> FindAsync(
        InstitutionId institutionId,
        string branchId,
        CancellationToken cancellationToken);
}

public interface ICurrencyReferenceSource
{
    Task<CurrencyReferenceSnapshot?> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}

/// <summary>Either the facts a decision may use, or the stable code explaining why they are unavailable.</summary>
public sealed record ResolvedEligibilityFacts(AuthoritativeProductEligibilityFacts? Facts, string? RejectionCode)
{
    public static ResolvedEligibilityFacts Resolved(AuthoritativeProductEligibilityFacts facts) => new(facts, null);

    public static ResolvedEligibilityFacts Rejected(string code) => new(null, code);
}

/// <summary>
/// Assembles the facts one account decision may use from their owners. It never accepts a
/// caller-supplied fact and never substitutes a last-known value. Decision: P1-E04-D17.
/// </summary>
public sealed class AuthoritativeEligibilityFactsResolver
{
    /// <summary>The approved ceiling. A source may declare shorter validity, never longer.</summary>
    public static readonly TimeSpan MaximumValidity = TimeSpan.FromSeconds(60);

    private const string ActiveBranchStatus = "active";

    private ResolvedEligibilityFacts Reject(string code)
    {
        _telemetry.Record(code);
        return ResolvedEligibilityFacts.Rejected(code);
    }

    private readonly ICustomerEligibilityFactsSource _customers;
    private readonly IBranchReferenceSource _branches;
    private readonly ICurrencyReferenceSource _currencies;
    private readonly IReferenceFactTelemetry _telemetry;

    public AuthoritativeEligibilityFactsResolver(
        ICustomerEligibilityFactsSource customers,
        IBranchReferenceSource branches,
        ICurrencyReferenceSource currencies,
        IReferenceFactTelemetry? telemetry = null)
    {
        _customers = customers ?? throw new ArgumentNullException(nameof(customers));
        _branches = branches ?? throw new ArgumentNullException(nameof(branches));
        _currencies = currencies ?? throw new ArgumentNullException(nameof(currencies));
        _telemetry = telemetry ?? NullReferenceFactTelemetry.Instance;
    }

    public async Task<ResolvedEligibilityFacts> ResolveAsync(
        InstitutionId institutionId,
        string customerId,
        string? branchId,
        string currencyCode,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);

        CustomerFactsLookup lookup = await _customers
            .GetAsync(institutionId, customerId, asOf, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Snapshot is null)
        {
            return Reject(lookup.RejectionCode ?? "eligibility_facts_unavailable");
        }

        CustomerEligibilitySnapshot customer = lookup.Snapshot;
        if (!string.Equals(customer.InstitutionId, institutionId.Value, StringComparison.Ordinal))
        {
            return Reject("eligibility_facts_mismatch");
        }

        if (customer.ValidUntil < asOf)
        {
            return Reject("eligibility_facts_stale");
        }

        if (customer.ValidUntil - customer.AsOf > MaximumValidity)
        {
            return Reject("eligibility_facts_window_exceeded");
        }

        if (string.IsNullOrWhiteSpace(branchId))
        {
            return Reject("branch_not_available");
        }

        BranchReferenceSnapshot? branch = await _branches
            .FindAsync(institutionId, branchId, cancellationToken)
            .ConfigureAwait(false);
        if (branch is null || !string.Equals(branch.Status, ActiveBranchStatus, StringComparison.OrdinalIgnoreCase))
        {
            return Reject("branch_not_available");
        }

        CurrencyReferenceSnapshot? currency = await _currencies
            .GetAsync(institutionId, asOf, cancellationToken)
            .ConfigureAwait(false);
        if (currency is null || string.IsNullOrWhiteSpace(currency.ReferenceVersion))
        {
            return Reject("currency_reference_unavailable");
        }

        string requestedCurrency = currencyCode.Trim().ToUpperInvariant();
        if (!currency.SupportedCurrencies.Contains(requestedCurrency, StringComparer.Ordinal))
        {
            return Reject("currency_not_supported");
        }

        if (string.IsNullOrWhiteSpace(customer.FactsVersion) || string.IsNullOrWhiteSpace(branch.ReferenceVersion))
        {
            return Reject("eligibility_facts_version_missing");
        }

        _telemetry.Record("resolved");
        return ResolvedEligibilityFacts.Resolved(new AuthoritativeProductEligibilityFacts(
            customer.FactsVersion,
            branch.ReferenceVersion,
            currency.ReferenceVersion,
            customer.ValidUntil,
            customer.CustomerType,
            customer.KycTier,
            customer.Segment,
            customer.RiskClass,
            customer.AgeYears,
            branch.BranchCode,
            ResidencyCountryCode: null,
            requestedCurrency));
    }
}

/// <summary>Fail-closed source used when no customer facts endpoint is configured.</summary>
public sealed class UnconfiguredCustomerEligibilityFactsSource : ICustomerEligibilityFactsSource
{
    public Task<CustomerFactsLookup> GetAsync(
        InstitutionId institutionId,
        string customerId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        Task.FromResult(CustomerFactsLookup.Rejected("eligibility_facts_unavailable"));
}
