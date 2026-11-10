using System;

namespace CoreBanking.CoreLedger.Contracts;

/// <summary>Request bodies for the institution account-numbering lifecycle. Decision: P1-E04-D06.</summary>
public sealed record CreateAccountNumberingDraftRequestV1(
    string VersionId,
    string SchemeId,
    int SchemeVersion,
    string Prefix,
    int ProductCodeWidth,
    int BranchCodeWidth,
    int SequenceWidth,
    string CheckDigit);

public sealed record SimulateAccountNumberingRequestV1(
    string SimulationId,
    string ContentHash);

public sealed record SubmitAccountNumberingRequestV1(
    string RequestId,
    string SourceRequestKey,
    string ContentHash);

public sealed record PublishAccountNumberingRequestV1(
    string PublicationId,
    string ContentHash,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record OpenAccountRequestV1(
    long ExpectedVersion);

public sealed record SubmitAccountApplicationForApprovalRequestV1(
    string RequestId,
    string SourceRequestKey,
    long ExpectedVersion);

public sealed record AccountActivationRequestV1(
    long ExpectedVersion);

public sealed record AccountActivationResponseV1(
    string InstitutionId,
    string AccountId,
    string MaskedAccountNumber,
    string Status,
    string LedgerAccountReference,
    long Version,
    DateTimeOffset ActivatedAt,
    bool Replayed);

public sealed record RevealAccountNumberRequestV1(
    string Reason);

/// <summary>
/// The only response that carries a full account number. It is returned for exactly one account to an
/// authorised, stepped-up caller and never appears in a list, event or log. Decision: P1-E04-D06.
/// </summary>
public sealed record AccountNumberRevealResponseV1(
    string InstitutionId,
    string AccountId,
    string AccountNumber,
    DateTimeOffset RevealedAt);
