using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreBanking.CoreLedger.Domain.Accounts;

/// <summary>
/// The approved control types, strongest first. The rank order is the approved precedence and is never
/// configurable. Decision: P1-E04-D10.
/// </summary>
public enum AccountControlType
{
    LegalBlock = 1,
    RegulatoryFreeze = 2,
    InstitutionFreeze = 3,
    TypedRestriction = 4,
    ProductPolicyRestriction = 5,
    Lien = 6,
    Notice = 7,
}

public enum AccountControlDirection
{
    Debit = 1,
    Credit = 2,
    Both = 3,
}

/// <summary>
/// One effective control as the decision reads it. A control never grants an ability: only prohibitive
/// types contribute to a denial, and a lien or notice only reports. Decision: P1-E04-D10.
/// </summary>
public sealed record AccountControlDescriptor(
    string ControlId,
    AccountControlType Type,
    string Status,
    AccountControlDirection Direction,
    IReadOnlyCollection<string> ChannelScopeCodes,
    IReadOnlyCollection<string> TransactionTypeScopeCodes,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool RequiresManualRelease)
{
    public const string ActiveStatus = "active";

    public bool IsActive => string.Equals(Status, ActiveStatus, StringComparison.Ordinal);

    /// <summary>Half-open effective period: the start is included, the end is excluded.</summary>
    public bool IsEffectiveAt(DateTimeOffset instant) =>
        EffectiveFrom <= instant && (EffectiveTo is null || instant < EffectiveTo);

    /// <summary>A lien reduces available balance and a notice is informational: neither denies. Decision: P1-E04-D10.</summary>
    public bool IsProhibitive => Type is not (AccountControlType.Lien or AccountControlType.Notice);

    public bool AppliesTo(string postingDirectionCode, string channelCode, string transactionTypeCode)
    {
        AccountControlDirection direction = ParseDirection(postingDirectionCode);
        bool directionMatches = Direction == AccountControlDirection.Both || Direction == direction;
        if (!directionMatches)
        {
            return false;
        }

        // An institution freeze denies debits by default and admits credits so the customer can still be funded.
        if (Type == AccountControlType.InstitutionFreeze && direction == AccountControlDirection.Credit)
        {
            return false;
        }

        bool channelMatches = ChannelScopeCodes.Count == 0
            || ChannelScopeCodes.Contains(channelCode, StringComparer.OrdinalIgnoreCase);
        bool typeMatches = TransactionTypeScopeCodes.Count == 0
            || TransactionTypeScopeCodes.Contains(transactionTypeCode, StringComparer.OrdinalIgnoreCase);
        return channelMatches && typeMatches;
    }

    public static AccountControlDirection ParseDirection(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "debit" => AccountControlDirection.Debit,
        "credit" => AccountControlDirection.Credit,
        "both" => AccountControlDirection.Both,
        _ => throw new ArgumentException("The posting direction must be debit, credit or both.", nameof(value)),
    };

    public static string ToToken(AccountControlDirection direction) => direction switch
    {
        AccountControlDirection.Debit => "debit",
        AccountControlDirection.Credit => "credit",
        AccountControlDirection.Both => "both",
        _ => throw new ArgumentException("The posting direction is not approved.", nameof(direction)),
    };

    public static string ToToken(AccountControlType type) => type switch
    {
        AccountControlType.LegalBlock => "legal_block",
        AccountControlType.RegulatoryFreeze => "regulatory_freeze",
        AccountControlType.InstitutionFreeze => "institution_freeze",
        AccountControlType.TypedRestriction => "typed_restriction",
        AccountControlType.ProductPolicyRestriction => "product_policy_restriction",
        AccountControlType.Lien => "lien",
        AccountControlType.Notice => "notice",
        _ => throw new ArgumentException("The control type is not approved.", nameof(type)),
    };
}

/// <summary>The intent the ledger asks the account module to decide. Amount is never used here.</summary>
public sealed record AccountControlIntent(
    string PostingDirectionCode,
    string TransactionTypeCode,
    string ChannelCode,
    DateTimeOffset EvaluationTime);

public static class AccountControlOutcomes
{
    public const string Allow = "allow";
    public const string Deny = "deny";
    public const string Review = "review";
    public const string VersionStaleCode = "control_version_stale";
    public const string ApplicableControlCode = "control_denied";
}

public sealed record AccountControlDecision(
    string Outcome,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<AccountControlDescriptor> EffectiveControls,
    IReadOnlyList<AccountControlDescriptor> DenyingControls)
{
    public bool Allowed => string.Equals(Outcome, AccountControlOutcomes.Allow, StringComparison.Ordinal);
}

/// <summary>
/// Deny-dominant evaluation over the approved precedence. One prohibitive control denies regardless of any
/// other control; a stale control version fails closed into <c>review</c>; reason codes are ordered and
/// deduplicated so the same inputs always produce the same evidence. Decision: P1-E04-D10.
/// </summary>
public static class AccountControlPrecedencePolicy
{
    public static AccountControlDecision Evaluate(
        IReadOnlyCollection<AccountControlDescriptor> controls,
        AccountControlIntent intent,
        long currentControlVersion,
        long knownControlVersion)
    {
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.PostingDirectionCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.TransactionTypeCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.ChannelCode);

        AccountControlDescriptor[] effective = controls
            .Where(control => control.IsActive && control.IsEffectiveAt(intent.EvaluationTime))
            .OrderBy(control => control.Type)
            .ThenBy(control => control.ControlId, StringComparer.Ordinal)
            .ToArray();

        AccountControlDescriptor[] denying = effective
            .Where(control => control.IsProhibitive
                && control.AppliesTo(intent.PostingDirectionCode, intent.ChannelCode, intent.TransactionTypeCode))
            .ToArray();

        List<string> reasonCodes = denying
            .Select(control => $"{AccountControlOutcomes.ApplicableControlCode}:{AccountControlDescriptor.ToToken(control.Type)}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        if (knownControlVersion != currentControlVersion)
        {
            // A caller that did not see the current control set cannot be trusted to proceed.
            reasonCodes.Insert(0, AccountControlOutcomes.VersionStaleCode);
            return new AccountControlDecision(AccountControlOutcomes.Review, reasonCodes, effective, denying);
        }

        if (denying.Length > 0)
        {
            // A control that a human may release yields review; an unconditional prohibition denies.
            bool allReleasable = denying.All(control => control.RequiresManualRelease);
            return new AccountControlDecision(
                allReleasable ? AccountControlOutcomes.Review : AccountControlOutcomes.Deny,
                reasonCodes,
                effective,
                denying);
        }

        return new AccountControlDecision(AccountControlOutcomes.Allow, reasonCodes, effective, denying);
    }
}
