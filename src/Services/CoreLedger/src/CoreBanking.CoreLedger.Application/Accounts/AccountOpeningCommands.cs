using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Accounts;

namespace CoreBanking.CoreLedger.Application.Accounts;

public sealed record OpenAccountCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string ApplicationId,
    long ExpectedVersion,
    string CorrelationId);

public sealed record AccountOpenedResult(
    string AccountId,
    string ApplicationId,
    string MaskedAccountNumber,
    string CustomerId,
    string ProductId,
    string ProductVersionId,
    string BranchId,
    string CurrencyCode,
    string Status,
    string LedgerInitialisationStatus,
    long Version,
    DateTimeOffset OpenedAt,
    bool Replayed);

public sealed record RevealAccountNumberCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string AccountId,
    string Reason,
    string CorrelationId);

public sealed record AccountNumberRevealResult(
    string AccountId,
    string AccountNumber,
    DateTimeOffset RevealedAt);

/// <summary>The published account-number scheme, or the stable code explaining why it is unavailable.</summary>
public sealed record AccountNumberSchemeLookup(
    AccountNumberScheme? Scheme,
    string? VersionId,
    string? RejectionCode)
{
    public static AccountNumberSchemeLookup Found(AccountNumberScheme scheme, string versionId) =>
        new(scheme, versionId, null);

    public static AccountNumberSchemeLookup Rejected(string code) => new(null, null, code);
}

public interface IAccountNumberSchemeSource
{
    Task<AccountNumberSchemeLookup> GetAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}

/// <summary>One allocated number with its masking form and depletion band.</summary>
public sealed record AllocatedAccountNumber(
    string AccountNumber,
    string MaskedAccountNumber,
    string SchemeId,
    string SchemeVersionId,
    long Sequence,
    int DepletionBand);

/// <summary>
/// Allocates from the durable reservation row of a published scheme instance. It must run inside the
/// caller's transaction so the number and the account commit together. Decision: P1-E04-D06.
/// </summary>
public interface IAccountNumberAllocator
{
    Task<AllocatedAccountNumber> AllocateAsync(
        InstitutionId institutionId,
        AccountNumberScheme scheme,
        string schemeVersionId,
        string productCode,
        string branchCode,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<long>> RetireAsync(
        InstitutionId institutionId,
        string schemeVersionId,
        IReadOnlyCollection<long> sequences,
        string reason,
        string actorId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reserves the number and creates the account in one atomic unit: reservation, account row, status
/// history, audit and the account-opened outbox fact. It never writes a balance, journal line or hold.
/// Decisions: P1-E04-D06, P1-E04-D09.
/// </summary>
public interface IAccountOpeningService
{
    Task<AccountOpenedResult> OpenAsync(OpenAccountCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// The atomic unit without its own transaction, for a caller that already owns one, such as the approval
    /// decision. Returns the encoded result. Decision: P1-E04-D09.
    /// </summary>
    Task<string?> OpenWithinTransactionAsync(OpenAccountCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Returns exactly one account's full number to an authorised caller and writes audit evidence that
/// excludes the number itself. Decision: P1-E04-D06.
/// </summary>
public interface IAccountNumberRevealService
{
    Task<AccountNumberRevealResult> RevealAsync(
        RevealAccountNumberCommand command,
        CancellationToken cancellationToken);
}
