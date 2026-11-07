using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Accounts;

namespace CoreBanking.CoreLedger.Application.Accounts;

/// <summary>
/// The result of asking the ledger to open its account for one bank account. A ledger account holds no
/// balance and no posting: only the reference and its status are relevant to the account module.
/// Decision: P1-E04-D09.
/// </summary>
public sealed record LedgerInitialisationResult(
    string AccountId,
    string LedgerAccountReference,
    string Status);

/// <summary>
/// The protected ledger contract the account module calls. It never creates a balance, journal line or
/// hold, and it answers with its own status rather than letting the caller assume success.
/// Decision: P1-E04-D09.
/// </summary>
public interface ILedgerAccountInitialisation
{
    Task<LedgerInitialisationResult> InitialiseAsync(
        InstitutionId institutionId,
        string accountId,
        string currencyCode,
        CancellationToken cancellationToken);
}

public sealed record SubmitAccountApplicationForApprovalCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string ApplicationId,
    long ExpectedVersion,
    string RequestId,
    string SourceRequestKey,
    string CorrelationId);

public sealed record SubmitAccountApplicationForApprovalResult(
    string ApplicationId,
    string RequestId,
    string EligibilityEvaluationId,
    string EligibilitySnapshotHash,
    string Status,
    long Version,
    string EventId,
    DateTimeOffset SubmittedAt);

public interface IAccountApprovalService
{
    Task<SubmitAccountApplicationForApprovalResult> SubmitForApprovalAsync(
        SubmitAccountApplicationForApprovalCommand command,
        CancellationToken cancellationToken);
}

public sealed record ActivateAccountCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    long ExpectedVersion,
    string CorrelationId);

public sealed record AccountActivatedResult(
    string AccountId,
    string MaskedAccountNumber,
    AccountApplicationStatus Status,
    string LedgerAccountReference,
    long Version,
    DateTimeOffset ActivatedAt,
    bool Replayed);

/// <summary>
/// Activation is a separate authorised transition. It re-reads authoritative facts inside the approved
/// freshness ceiling, requires a proven ledger initialisation and a bound approval, and never posts money.
/// Decision: P1-E04-D09.
/// </summary>
public interface IAccountActivationService
{
    Task<AccountActivatedResult> ActivateAsync(
        ActivateAccountCommand command,
        CancellationToken cancellationToken);
}
