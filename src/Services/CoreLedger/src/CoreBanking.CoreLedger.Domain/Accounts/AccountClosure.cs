using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CoreBanking.CoreLedger.Domain.Accounts;

public enum AccountClosureProposalStatus
{
    ConditionsMet,
    ConditionsFailed,
    PendingApproval,
    PendingClosure,
    Closed,
    Rejected,
}

/// <summary>
/// The ledger's answer to the closure condition question. The account module records it as evidence and
/// performs no balance arithmetic of its own. Decision: P1-E04-D13.
/// </summary>
public sealed record LedgerClosureConditions(
    string LedgerAccountReference,
    string SnapshotVersion,
    DateTimeOffset AsOf,
    string CurrencyCode,
    decimal LedgerBalance,
    decimal AvailableBalance,
    decimal Holds,
    decimal OutstandingLienAmount,
    int PendingSettlements);

/// <summary>The conditions the account module itself owns: controls, disputes and notices. Decision: P1-E04-D13.</summary>
public sealed record AccountClosureFacts(
    bool LegalControlActive,
    decimal StatutoryOrTaxLienOutstanding,
    decimal InstitutionOrCustomerLienOutstanding,
    bool DisputeOpen,
    bool NoticePending,
    bool LienReleaseDecisionRecorded);

public sealed record AccountClosureConditionResult(
    bool Settled,
    IReadOnlyList<string> RefusalCodes,
    string SnapshotHash);

/// <summary>
/// The approved closure conditions: a settled ledger balance with no holds, pending settlements or outstanding
/// liens, no active legal or regulatory control and no open dispute, evaluated against a live ledger response
/// inside the freshness ceiling. Nothing here forces a balance to zero. Decision: P1-E04-D13.
/// </summary>
public static class AccountClosurePolicy
{
    public const string BalanceNotSettledCode = "closure_balance_not_settled";
    public const string FundsNotSettledCode = "closure_funds_not_settled";
    public const string LegalControlActiveCode = "closure_legal_control_active";
    public const string LienOutstandingCode = "closure_lien_outstanding";
    public const string DisputeOpenCode = "closure_dispute_open";
    public const string NoticePendingCode = "closure_notice_pending";
    public const string BalanceSourceStaleCode = "balance_source_stale";
    public const string ProposalConditionsNotMetCode = "account_closure_conditions_not_met";
    public const string ProposalNotPendingApprovalCode = "account_closure_proposal_not_pending_approval";
    public const string RecheckRequiredCode = "account_closure_recheck_required";

    public static AccountClosureConditionResult Evaluate(
        LedgerClosureConditions conditions,
        AccountClosureFacts facts,
        DateTimeOffset now,
        TimeSpan freshnessCeiling)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(facts);

        List<string> codes = [];
        if (conditions.AsOf > now || now - conditions.AsOf > freshnessCeiling)
        {
            codes.Add(BalanceSourceStaleCode);
        }

        if (conditions.LedgerBalance != 0m)
        {
            codes.Add(BalanceNotSettledCode);
        }

        if (conditions.Holds != 0m || conditions.PendingSettlements > 0)
        {
            codes.Add(FundsNotSettledCode);
        }

        if (conditions.OutstandingLienAmount != 0m
            || facts.StatutoryOrTaxLienOutstanding != 0m
            || (facts.InstitutionOrCustomerLienOutstanding != 0m && !facts.LienReleaseDecisionRecorded))
        {
            codes.Add(LienOutstandingCode);
        }

        if (facts.LegalControlActive)
        {
            codes.Add(LegalControlActiveCode);
        }

        if (facts.DisputeOpen)
        {
            codes.Add(DisputeOpenCode);
        }

        if (facts.NoticePending)
        {
            codes.Add(NoticePendingCode);
        }

        string[] ordered = [.. codes.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal)];
        return new AccountClosureConditionResult(ordered.Length == 0, ordered, SnapshotHash(conditions));
    }

    /// <summary>
    /// The decision binds to this hash, so a later condition snapshot can never be substituted for the one the
    /// approver saw. Decision: P1-E04-D13.
    /// </summary>
    public static string SnapshotHash(LedgerClosureConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        string canonical = string.Join('|',
            "account-closure-conditions.v1",
            conditions.LedgerAccountReference,
            conditions.SnapshotVersion,
            conditions.AsOf.ToString("O", CultureInfo.InvariantCulture),
            conditions.CurrencyCode,
            conditions.LedgerBalance.ToString("F4", CultureInfo.InvariantCulture),
            conditions.AvailableBalance.ToString("F4", CultureInfo.InvariantCulture),
            conditions.Holds.ToString("F4", CultureInfo.InvariantCulture),
            conditions.OutstandingLienAmount.ToString("F4", CultureInfo.InvariantCulture),
            conditions.PendingSettlements.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>
/// The classified operations that may still post against a closed account. The listed set is the only
/// exception to <c>account_closed</c>; an unclassified operation is refused. Decision: P1-E04-D13.
/// </summary>
public static class AccountPostClosePolicy
{
    public static IReadOnlySet<string> PermittedOperations { get; } = new HashSet<string>(
        [
            "correction_bank_error",
            "late_settlement",
            "chargeback_settlement",
            "dispute_settlement",
            "legal_block_record",
        ],
        StringComparer.Ordinal);

    /// <summary>
    /// True when the operation may post against the given account status. Only a closed account is restricted
    /// here; every other status is decided by the control precedence. Decision: P1-E04-D13.
    /// </summary>
    public static bool IsPermitted(AccountStatus status, string operationCode)
    {
        if (status != AccountStatus.Closed)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(operationCode)
            && PermittedOperations.Contains(operationCode.Trim().ToLowerInvariant());
    }
}
