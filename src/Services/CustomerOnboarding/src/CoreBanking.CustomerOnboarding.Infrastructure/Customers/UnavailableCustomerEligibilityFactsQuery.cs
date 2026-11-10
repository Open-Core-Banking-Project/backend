using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CustomerOnboarding.Application.Customers;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Customers;

/// <summary>
/// Fail-closed stand-in used when the service has no relational store or no protection keys. It never
/// invents a fact, so the internal route answers unavailable instead of guessing. Decision: P1-E04-D17.
/// </summary>
public sealed class UnavailableCustomerEligibilityFactsQuery : ICustomerEligibilityFactsQuery
{
    public Task<CustomerEligibilityFacts?> GetAsync(
        string institutionId,
        string customerId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        throw new CustomerEligibilityFactsException(
            "customer_facts_unavailable",
            "Authoritative customer facts are not configured on this host.");
}
