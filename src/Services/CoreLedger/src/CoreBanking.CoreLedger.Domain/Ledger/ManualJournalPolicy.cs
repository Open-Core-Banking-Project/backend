using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CoreBanking.CoreLedger.Domain.Institutions;

namespace CoreBanking.CoreLedger.Domain.Ledger;

/// <summary>
/// One account class the institution approves for manual journals, the chart roles it covers and the extra
/// approval it needs. A class is configuration, never a code default. Decision: P1-E05-D11.
/// </summary>
public sealed record ManualJournalClassGrant(
    string Code,
    IReadOnlyList<LedgerAccountRole> Roles,
    bool RequiresCompensatingAccount,
    int MinimumApprovers);

/// <summary>One amount tier. A null ceiling is the final, unbounded tier. Decision: P1-E05-D11.</summary>
public sealed record ManualJournalTier(decimal? UpToAmount, int RequiredApprovers, bool RequiresFinanceHead);

/// <summary>What one manual journal needs before it may post. Decision: P1-E05-D11.</summary>
public sealed record ManualJournalApprovalRequirement(int RequiredApprovers, bool RequiresFinanceHead);

/// <summary>
/// The institution's effective <c>ledger.manual_journal_policy</c> content: the bounded reason catalogue, the
/// account-class grants and the amount tiers. No default policy ships; without a published policy every manual
/// journal is refused. Decision: P1-E05-D11.
/// </summary>
public sealed record ManualJournalPolicy(
    string PolicyId,
    int Version,
    string FinanceHeadPermission,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<ManualJournalClassGrant> Classes,
    IReadOnlyList<ManualJournalTier> Tiers)
{
    public const string Area = "ledger.manual_journal_policy";
    public const string Schema = "ledger.manual_journal_policy.v1";

    public const string CustomerAccountAdjustmentClass = "customer_account_adjustment";

    private const int MaximumReasonCodeLength = 64;
    private const int MaximumClassCount = 16;
    private const int MaximumTierCount = 8;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public ConfigurationContent ToConfigurationContent() => ConfigurationContent.Create(JsonSerializer.Serialize(
        new
        {
            policyId = PolicyId,
            version = Version,
            financeHeadPermission = FinanceHeadPermission,
            reasonCodes = ReasonCodes.ToArray(),
            classes = Classes.Select(grant => new
            {
                code = grant.Code,
                roles = grant.Roles.Select(role => RoleCode(role)).ToArray(),
                requiresCompensatingAccount = grant.RequiresCompensatingAccount,
                minimumApprovers = grant.MinimumApprovers,
            }).ToArray(),
            tiers = Tiers.Select(tier => new
            {
                upToAmount = tier.UpToAmount,
                requiredApprovers = tier.RequiredApprovers,
                requiresFinanceHead = tier.RequiresFinanceHead,
            }).ToArray(),
        },
        SerializerOptions));

    public static ManualJournalPolicy Create(
        string policyId,
        int version,
        string financeHeadPermission,
        IEnumerable<string> reasonCodes,
        IEnumerable<ManualJournalClassGrant> classes,
        IEnumerable<ManualJournalTier> tiers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyId);
        string id = policyId.Trim();
        if (id.Length > 64)
        {
            throw new ArgumentException("A policy identifier may contain at most 64 characters.", nameof(policyId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(financeHeadPermission);
        string headPermission = financeHeadPermission.Trim();
        if (headPermission.Length > 128 || headPermission.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A permission identifier is required.", nameof(financeHeadPermission));
        }

        string[] reasons = NormaliseCodes(reasonCodes, nameof(reasonCodes), MaximumReasonCodeLength);
        if (reasons.Length == 0)
        {
            throw new ArgumentException("At least one approved reason code is required.", nameof(reasonCodes));
        }

        ManualJournalClassGrant[] grants = (classes ?? throw new ArgumentNullException(nameof(classes))).ToArray();
        if (grants.Length == 0 || grants.Length > MaximumClassCount)
        {
            throw new ArgumentException("Between one and sixteen account classes are required.", nameof(classes));
        }

        if (grants.Select(grant => grant.Code).Distinct(StringComparer.Ordinal).Count() != grants.Length)
        {
            throw new ArgumentException("An account class may be declared only once.", nameof(classes));
        }

        foreach (ManualJournalClassGrant grant in grants)
        {
            if (string.IsNullOrWhiteSpace(grant.Code) || grant.Code.Trim().Length > 64)
            {
                throw new ArgumentException("An account class requires a bounded code.", nameof(classes));
            }

            if (grant.Roles is null || grant.Roles.Count == 0)
            {
                throw new ArgumentException("An account class must cover at least one chart role.", nameof(classes));
            }

            if (grant.MinimumApprovers < 1 || grant.MinimumApprovers > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(classes), "A class requires between one and three approvers.");
            }
        }

        LedgerAccountRole[] covered = grants.SelectMany(grant => grant.Roles).ToArray();
        if (covered.Distinct().Count() != covered.Length)
        {
            throw new ArgumentException("A chart role may belong to only one account class.", nameof(classes));
        }

        ManualJournalTier[] tierArray = (tiers ?? throw new ArgumentNullException(nameof(tiers))).ToArray();
        if (tierArray.Length == 0 || tierArray.Length > MaximumTierCount)
        {
            throw new ArgumentException("Between one and eight amount tiers are required.", nameof(tiers));
        }

        decimal previousCeiling = 0m;
        for (int index = 0; index < tierArray.Length; index++)
        {
            ManualJournalTier tier = tierArray[index];
            if (tier.RequiredApprovers < 1 || tier.RequiredApprovers > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(tiers), "A tier requires between one and three approvers.");
            }

            bool last = index == tierArray.Length - 1;
            if (last != (tier.UpToAmount is null))
            {
                throw new ArgumentException("Only the final tier may be unbounded.", nameof(tiers));
            }

            if (tier.UpToAmount is decimal ceiling)
            {
                if (ceiling <= previousCeiling)
                {
                    throw new ArgumentException("Tier ceilings must increase strictly.", nameof(tiers));
                }

                previousCeiling = ceiling;
            }
        }

        return new ManualJournalPolicy(id, version, headPermission, reasons, grants, tierArray);
    }

    public static ManualJournalPolicy Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        PolicyPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<PolicyPayload>(json, SerializerOptions)
                ?? throw new ArgumentException("Manual journal policy content is required.", nameof(json));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Manual journal policy content does not match its schema.", nameof(json), exception);
        }

        return Create(
            payload.PolicyId,
            payload.Version,
            payload.FinanceHeadPermission,
            payload.ReasonCodes ?? [],
            (payload.Classes ?? []).Select(item => new ManualJournalClassGrant(
                item.Code,
                (item.Roles ?? []).Select(role => ParseRole(role)).ToArray(),
                item.RequiresCompensatingAccount,
                item.MinimumApprovers)),
            (payload.Tiers ?? []).Select(item =>
                new ManualJournalTier(item.UpToAmount, item.RequiredApprovers, item.RequiresFinanceHead)));
    }

    public bool AcceptsReason(string reasonCode) =>
        !string.IsNullOrWhiteSpace(reasonCode)
        && ReasonCodes.Contains(reasonCode.Trim(), StringComparer.Ordinal);

    public ManualJournalClassGrant? ClassForRole(LedgerAccountRole role) =>
        Classes.SingleOrDefault(grant => grant.Roles.Contains(role));

    /// <summary>
    /// The approval the journal needs: the amount tier plus every class minimum, so a customer-account line always
    /// needs at least two approvers. Decision: P1-E05-D11.
    /// </summary>
    public ManualJournalApprovalRequirement RequirementFor(decimal totalAmount, IEnumerable<string> accountClasses)
    {
        if (totalAmount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(totalAmount), "A manual journal total must be positive.");
        }

        string[] classCodes = (accountClasses ?? throw new ArgumentNullException(nameof(accountClasses)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (classCodes.Length == 0)
        {
            throw new ArgumentException("At least one account class is required.", nameof(accountClasses));
        }

        ManualJournalTier tier = Tiers.FirstOrDefault(item => item.UpToAmount is null || totalAmount <= item.UpToAmount)
            ?? throw new LedgerPostingException("manual_journal_tier_unresolved");
        int required = tier.RequiredApprovers;
        foreach (string classCode in classCodes)
        {
            ManualJournalClassGrant grant = Classes.SingleOrDefault(item => item.Code == classCode)
                ?? throw new LedgerPostingException("manual_journal_class_not_approved");
            required = Math.Max(required, grant.MinimumApprovers);
        }

        return new ManualJournalApprovalRequirement(required, tier.RequiresFinanceHead);
    }

    /// <summary>
    /// Whether one decision satisfies the approved quorum: enough distinct approvers, a Finance head where the tier
    /// demands it, and never the maker. Decision: P1-E05-D11.
    /// </summary>
    public static bool IsSatisfiedBy(
        ManualJournalApprovalRequirement requirement,
        IEnumerable<ManualJournalApproval> approvals,
        string makerActorId)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(approvals);
        ArgumentException.ThrowIfNullOrWhiteSpace(makerActorId);

        ManualJournalApproval[] distinct = approvals
            .GroupBy(approval => approval.ApproverActorId, StringComparer.Ordinal)
            .Select(group => group.OrderBy(approval => approval.DecidedAt).Last())
            .Where(approval => !string.Equals(approval.ApproverActorId, makerActorId.Trim(), StringComparison.Ordinal))
            .ToArray();
        if (distinct.Length < requirement.RequiredApprovers)
        {
            return false;
        }

        return !requirement.RequiresFinanceHead
            || distinct.Any(approval => approval.IsFinanceHead);
    }

    private static string RoleCode(LedgerAccountRole role) => role switch
    {
        LedgerAccountRole.CustomerDeposit => "customerDeposit",
        LedgerAccountRole.CashSettlement => "cashSettlement",
        LedgerAccountRole.InternalTransferClearing => "internalTransferClearing",
        LedgerAccountRole.FeeIncome => "feeIncome",
        LedgerAccountRole.CommissionPayable => "commissionPayable",
        LedgerAccountRole.TaxPayable => "taxPayable",
        LedgerAccountRole.InterestExpense => "interestExpense",
        LedgerAccountRole.PenaltyIncome => "penaltyIncome",
        LedgerAccountRole.Suspense => "suspense",
        _ => throw new ArgumentOutOfRangeException(nameof(role), "Unsupported chart role."),
    };

    private static LedgerAccountRole ParseRole(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A chart role is required.", nameof(value));
        }

        return value.Trim() switch
        {
            "customerDeposit" => LedgerAccountRole.CustomerDeposit,
            "cashSettlement" => LedgerAccountRole.CashSettlement,
            "internalTransferClearing" => LedgerAccountRole.InternalTransferClearing,
            "feeIncome" => LedgerAccountRole.FeeIncome,
            "commissionPayable" => LedgerAccountRole.CommissionPayable,
            "taxPayable" => LedgerAccountRole.TaxPayable,
            "interestExpense" => LedgerAccountRole.InterestExpense,
            "penaltyIncome" => LedgerAccountRole.PenaltyIncome,
            "suspense" => LedgerAccountRole.Suspense,
            _ => throw new ArgumentException("Unsupported chart role.", nameof(value)),
        };
    }

    private static string[] NormaliseCodes(IEnumerable<string> values, string name, int maximumLength)
    {
        string[] codes = (values ?? throw new ArgumentNullException(name))
            .Select(value => value?.Trim() ?? string.Empty)
            .ToArray();
        if (codes.Any(code => code.Length == 0 || code.Length > maximumLength))
        {
            throw new ArgumentException($"Every code must be between 1 and {maximumLength} characters.", name);
        }

        if (codes.Distinct(StringComparer.Ordinal).Count() != codes.Length)
        {
            throw new ArgumentException("A code may be declared only once.", name);
        }

        return codes;
    }

    private sealed record PolicyPayload(
        string PolicyId,
        int Version,
        string FinanceHeadPermission,
        string[]? ReasonCodes,
        PolicyClassPayload[]? Classes,
        PolicyTierPayload[]? Tiers);

    private sealed record PolicyClassPayload(
        string Code,
        string[]? Roles,
        bool RequiresCompensatingAccount,
        int MinimumApprovers);

    private sealed record PolicyTierPayload(decimal? UpToAmount, int RequiredApprovers, bool RequiresFinanceHead);
}
