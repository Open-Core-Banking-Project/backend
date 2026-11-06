using System;
using System.Collections.Generic;

namespace CoreBanking.CoreLedger.Contracts;

public sealed record CreateAccountApplicationRequestV1(
    string RequestId,
    string CustomerId,
    string ProductId,
    string BranchId,
    string Currency,
    string AccountPurposeCode,
    string OwnershipTypeCode);

public sealed record SubmitAccountApplicationRequestV1(
    long ExpectedVersion,
    string ReasonCode);

public sealed record AccountApplicationResponseV1(
    string InstitutionId,
    string ApplicationId,
    string RequestId,
    string CustomerId,
    string ProductId,
    string ProductVersionId,
    string BranchId,
    string Currency,
    string Status,
    string EligibilitySnapshotId,
    long Version,
    DateTimeOffset ChangedAt,
    bool Replayed);

public sealed record ActivateAccountRequestV1(
    string ApplicationId,
    long ExpectedVersion);

public sealed record AccountResponseV1(
    string InstitutionId,
    string AccountId,
    string MaskedAccountNumber,
    string CustomerId,
    string ProductId,
    string ProductVersionId,
    string BranchId,
    string Currency,
    string Status,
    string LedgerInitialisationStatus,
    long Version,
    DateTimeOffset ChangedAt,
    bool Replayed);

public sealed record MonetaryAmountV1(
    decimal Amount,
    string Currency);

public sealed record BalanceSourceV1(
    string LedgerAccountReference,
    string SnapshotVersion,
    DateTimeOffset AsOf);

public sealed record AccountBlockSummaryV1(
    string BlockId,
    string ScopeCode,
    string Status);

public sealed record AccountBalanceSummaryV1(
    MonetaryAmountV1 LedgerBalance,
    MonetaryAmountV1 AvailableBalance,
    MonetaryAmountV1 Holds,
    MonetaryAmountV1 Liens,
    IReadOnlyCollection<AccountBlockSummaryV1> Blocks,
    BalanceSourceV1 Source);

public sealed record AccountSummaryResponseV1(
    string InstitutionId,
    string AccountId,
    string MaskedAccountNumber,
    string CustomerId,
    string ProductId,
    string ProductVersionId,
    string BranchId,
    string Currency,
    string Status,
    long AccountVersion,
    string BalanceAvailability,
    AccountBalanceSummaryV1? Balances);

public sealed record AccountStatusProposalRequestV1(
    string RequestId,
    string ProposedStatus,
    string ReasonCode,
    string? EvidenceReference,
    long ExpectedVersion);

public sealed record AccountStatusProposalResponseV1(
    string InstitutionId,
    string ProposalId,
    string AccountId,
    string ProposedStatus,
    string Status,
    long AccountVersion,
    DateTimeOffset ChangedAt,
    bool Replayed);

public sealed record AccountOpenedV1(
    string InstitutionId,
    string AccountId,
    string MaskedAccountNumber,
    string CustomerId,
    string ProductId,
    string ProductVersionId,
    string BranchId,
    string Currency,
    string Status,
    long AccountVersion,
    DateTimeOffset OpenedAt);

public sealed record AccountActivatedV1(
    string InstitutionId,
    string AccountId,
    string CustomerId,
    string ProductVersionId,
    long AccountVersion,
    DateTimeOffset ActivatedAt);

public sealed record AccountStatusChangedV1(
    string InstitutionId,
    string AccountId,
    string PreviousStatus,
    string Status,
    string ReasonCode,
    long ControlVersion,
    long AccountVersion,
    DateTimeOffset ChangedAt);

public sealed record AccountPostingContextV1(
    string InstitutionId,
    string AccountId,
    string CustomerId,
    string ProductVersionId,
    string Currency,
    string AccountStatus,
    string LedgerInitialisationStatus,
    long AccountVersion,
    long ControlVersion,
    DateTimeOffset AsOf);
