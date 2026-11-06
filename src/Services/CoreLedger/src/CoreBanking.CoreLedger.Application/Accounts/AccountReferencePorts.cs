using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Accounts;

namespace CoreBanking.CoreLedger.Application.Accounts;

/// <summary>The effective institution opening ceiling, or the stable code when it is unavailable.</summary>
public sealed record AccountOpeningPolicyLookup(
    AccountOpeningPolicy? Policy,
    string? ReferenceVersion,
    string? RejectionCode)
{
    public static AccountOpeningPolicyLookup Found(AccountOpeningPolicy policy, string referenceVersion) =>
        new(policy, referenceVersion, null);

    public static AccountOpeningPolicyLookup Rejected(string code) => new(null, null, code);
}

/// <summary>Resolves the published <c>institution.account-opening-policy</c> area. Decision: P1-E04-D07.</summary>
public interface IAccountOpeningPolicySource
{
    Task<AccountOpeningPolicyLookup> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reads the holdings that count against a multiple-account limit, in the counted statuses only.
/// The reading source is authoritative: a caller never supplies its own count. Decision: P1-E04-D07.
/// </summary>
public interface IAccountHoldingsReader
{
    Task<IReadOnlyCollection<AccountHolding>> GetAsync(
        InstitutionId institutionId,
        string customerId,
        CancellationToken cancellationToken);
}

/// <summary>Fail-closed source used when the configuration store is not available to the caller.</summary>
public sealed class UnconfiguredAccountOpeningPolicySource : IAccountOpeningPolicySource
{
    public static UnconfiguredAccountOpeningPolicySource Instance { get; } = new();

    public Task<AccountOpeningPolicyLookup> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        Task.FromResult(AccountOpeningPolicyLookup.Rejected("account_opening_policy_unavailable"));
}
