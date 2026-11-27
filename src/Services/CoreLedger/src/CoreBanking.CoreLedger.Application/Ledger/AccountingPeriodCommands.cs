using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Application.Ledger;

public sealed record AccountingPeriodApprovalInput(
    string ApproverActorId,
    string ApprovalReference,
    DateTimeOffset ApprovedAt);

public sealed record OpenAccountingPeriodCommand(
    InstitutionId InstitutionId,
    string LegalEntityCode,
    int Year,
    int Month,
    ActorId ActorId,
    string CorrelationId,
    string? Note = null);

public sealed record SoftCloseAccountingPeriodCommand(
    InstitutionId InstitutionId,
    string LegalEntityCode,
    int Year,
    int Month,
    ActorId ActorId,
    long ExpectedVersion,
    IReadOnlyList<string> AllowedEventCodes,
    string ApprovalReference,
    string ReasonCode,
    string CorrelationId);

public sealed record CloseAccountingPeriodCommand(
    InstitutionId InstitutionId,
    string LegalEntityCode,
    int Year,
    int Month,
    ActorId ActorId,
    long ExpectedVersion,
    decimal DebitTotal,
    decimal CreditTotal,
    int BlockingReconciliationExceptions,
    bool RequiredProcessingStepsCompleted,
    bool OutboxHealthy,
    string ApprovalReference,
    string SummaryHash,
    string CorrelationId);

public sealed record ReopenAccountingPeriodCommand(
    InstitutionId InstitutionId,
    string LegalEntityCode,
    int Year,
    int Month,
    ActorId ActorId,
    long ExpectedVersion,
    IReadOnlyList<string> AllowedEventCodes,
    IReadOnlyList<AccountingPeriodApprovalInput> Approvals,
    DateTimeOffset ExpiresAt,
    string ReasonCode,
    string CorrelationId);

public sealed record RecloseAccountingPeriodCommand(
    InstitutionId InstitutionId,
    string LegalEntityCode,
    int Year,
    int Month,
    ActorId ActorId,
    long ExpectedVersion,
    string ReconciliationReference,
    string CorrelationId);

public sealed record LockAccountingPeriodCommand(
    InstitutionId InstitutionId,
    string LegalEntityCode,
    int Year,
    int Month,
    ActorId ActorId,
    long ExpectedVersion,
    string ApprovalReference,
    string CorrelationId);

public sealed record AccountingPeriodResult(
    string InstitutionId,
    string LegalEntityCode,
    string PeriodCode,
    string Status,
    IReadOnlyList<string> AllowedEventCodes,
    long Version,
    decimal? ClosedDebitTotal,
    decimal? ClosedCreditTotal,
    string? CloseSummaryHash,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? ReopenExpiresAt,
    DateTimeOffset? ReclosedAt,
    string? RecloseReconciliationReference,
    DateTimeOffset? LockedAt,
    DateTimeOffset UpdatedAt,
    bool Replayed);

public sealed record RecalculationHandoffResult(
    string InstitutionId,
    string LegalEntityCode,
    string PeriodCode,
    string HandoffId,
    string FinancialTransactionId,
    string ValueDate,
    int AgeDays,
    string ReasonCode,
    string EvidenceReference,
    string ApprovalReference,
    DateTimeOffset RecordedAt);

/// <summary>
/// Owns the approved accounting-period lifecycle and exposes the durable recalculation handoffs a historical value
/// date creates. Every transition is versioned and serialized against posting. Decision: P1-E05-D13.
/// </summary>
public interface IAccountingPeriodService
{
    Task<AccountingPeriodResult> OpenAsync(OpenAccountingPeriodCommand command, CancellationToken cancellationToken);

    Task<AccountingPeriodResult> SoftCloseAsync(SoftCloseAccountingPeriodCommand command, CancellationToken cancellationToken);

    Task<AccountingPeriodResult> CloseAsync(CloseAccountingPeriodCommand command, CancellationToken cancellationToken);

    Task<AccountingPeriodResult> ReopenAsync(ReopenAccountingPeriodCommand command, CancellationToken cancellationToken);

    Task<AccountingPeriodResult> RecloseAsync(RecloseAccountingPeriodCommand command, CancellationToken cancellationToken);

    Task<AccountingPeriodResult> LockAsync(LockAccountingPeriodCommand command, CancellationToken cancellationToken);

    Task<AccountingPeriodResult> GetAsync(
        InstitutionId institutionId,
        string legalEntityCode,
        int year,
        int month,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecalculationHandoffResult>> GetRecalculationHandoffsAsync(
        InstitutionId institutionId,
        int year,
        int month,
        CancellationToken cancellationToken);
}
