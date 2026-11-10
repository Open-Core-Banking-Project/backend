using System;

namespace CoreBanking.CoreLedger.Contracts;

public sealed record CreateStatementRequestV1(
    string RequestId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string FormatCode,
    string PurposeCode);

public sealed record StatementRequestResponseV1(
    string InstitutionId,
    string StatementRequestId,
    string AccountId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string FormatCode,
    string Status,
    string LedgerQueryReference,
    DateTimeOffset RequestedAt,
    bool Replayed);

public sealed record StatementRequestedV1(
    string InstitutionId,
    string StatementRequestId,
    string AccountId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string FormatCode,
    string RequesterTypeCode,
    string LedgerQueryReference,
    string CorrelationId,
    DateTimeOffset RequestedAt);

public sealed record StatementDocumentReadyV1(
    string InstitutionId,
    string StatementRequestId,
    string AccountId,
    string ArtifactReference,
    string IntegrityReference,
    string Status,
    DateTimeOffset ReadyAt);
