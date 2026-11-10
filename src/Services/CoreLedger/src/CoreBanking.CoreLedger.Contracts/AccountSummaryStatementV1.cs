using System;
using System.Collections.Generic;

namespace CoreBanking.CoreLedger.Contracts;

/// <summary>Request bodies for the institution statement-policy lifecycle. Decision: P1-E04-D15.</summary>
public sealed record CreateStatementPolicyDraftRequestV1(
    string VersionId,
    string PolicyId,
    int PolicyVersion,
    int MaximumSpanDays,
    int RetentionDays,
    int ArtifactReferenceMinutes,
    IReadOnlyCollection<string> Formats,
    IReadOnlyCollection<string> Purposes,
    IReadOnlyCollection<string> ReceiptFormats,
    IReadOnlyCollection<string> PeriodKinds);

public sealed record SimulateStatementPolicyRequestV1(string SimulationId, string ContentHash);

public sealed record SubmitStatementPolicyRequestV1(string RequestId, string SourceRequestKey, string ContentHash);

public sealed record PublishStatementPolicyRequestV1(
    string PublicationId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record CreateAccountStatementRequestV1(
    string RequestId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string FormatCode,
    string PurposeCode,
    string RequesterType);

public sealed record AccountStatementRequestResponseV1(
    string InstitutionId,
    string RequestId,
    string AccountId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string FormatCode,
    string PurposeCode,
    string Status,
    string LedgerQueryReference,
    string IntegrityReference,
    string? ArtifactReference,
    DateTimeOffset? ArtifactReadyAt,
    IReadOnlyCollection<string> RefusalCodes,
    string RequesterType,
    DateTimeOffset RequestedAt,
    bool Replayed);

public sealed record AuthoriseStatementArtifactRequestV1(string RequesterType);

/// <summary>
/// A short-lived artifact reference. It carries no statement content: content stays with the document capability and
/// is never returned in a response body. Decision: P1-E04-D15.
/// </summary>
public sealed record StatementArtifactReferenceResponseV1(
    string InstitutionId,
    string RequestId,
    string AccountId,
    string ArtifactReference,
    string IntegrityReference,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RetentionExpiresAt,
    string Status,
    bool Replayed);
