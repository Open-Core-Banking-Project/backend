using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CoreLedger.Domain.Accounts;

namespace CoreBanking.CoreLedger.Application.Accounts;

/// <summary>
/// The ledger's closure-condition answer. It carries the source reference, snapshot version and as-of so the
/// account module can prove freshness and record the evidence it decided on. Decision: P1-E04-D13.
/// </summary>
public interface ILedgerClosureConditionSource
{
    Task<LedgerClosureConditions> GetAsync(
        InstitutionId institutionId,
        string accountId,
        string currencyCode,
        CancellationToken cancellationToken);
}

public sealed record CreateAccountClosureProposalCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    string RequestId,
    string ReasonCode,
    string? SupportingReference,
    bool LienReleaseDecisionRecorded,
    bool DisputeOpen,
    bool NoticePending,
    string CorrelationId);

public sealed record SubmitAccountClosureProposalCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    string ProposalId,
    long ExpectedVersion,
    string RequestId,
    string SourceRequestKey,
    string CorrelationId);

public sealed record RecheckAccountClosureCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string AccountId,
    string ProposalId,
    long ExpectedVersion,
    string CorrelationId);

public sealed record AccountClosureProposalResult(
    string InstitutionId,
    string ProposalId,
    string AccountId,
    string RequestId,
    string Status,
    string ReasonCode,
    string? SupportingReference,
    IReadOnlyList<string> RefusalCodes,
    string ConditionSnapshotHash,
    string LedgerAccountReference,
    string LedgerSnapshotVersion,
    DateTimeOffset ConditionsAsOf,
    decimal LedgerBalance,
    decimal AvailableBalance,
    decimal Holds,
    decimal OutstandingLienAmount,
    int PendingSettlements,
    string? ApprovalRequestId,
    DateTimeOffset ChangedAt,
    long Version,
    bool Replayed);

/// <summary>
/// Owns the approved closure ordering: propose without changing the status, record the condition snapshot,
/// submit for approval bound to that snapshot, move to pending closure on approval and close only after a final
/// re-check. The module never forces a balance to zero. Decision: P1-E04-D13.
/// </summary>
public interface IAccountClosureService
{
    Task<AccountClosureProposalResult> ProposeAsync(
        CreateAccountClosureProposalCommand command,
        CancellationToken cancellationToken);

    Task<AccountClosureProposalResult> SubmitForApprovalAsync(
        SubmitAccountClosureProposalCommand command,
        CancellationToken cancellationToken);

    Task<AccountClosureProposalResult> RecheckAsync(
        RecheckAccountClosureCommand command,
        CancellationToken cancellationToken);

    Task<AccountClosureProposalResult> GetAsync(
        InstitutionId institutionId,
        string accountId,
        string proposalId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Applies an approved closure decision to the exact submitted proposal. It runs inside the decision
/// consumer's transaction so the pending-closure and closed transitions commit with the decision.
/// Decision: P1-E04-D13.
/// </summary>
public interface IAccountClosureApprovalApplier
{
    Task<string> ApplyDecisionAsync(
        InstitutionId institutionId,
        string proposalId,
        string requestId,
        string subjectVersion,
        string subjectHash,
        bool approved,
        string approverActorId,
        DateTimeOffset decidedAt,
        string correlationId,
        CancellationToken cancellationToken);
}
