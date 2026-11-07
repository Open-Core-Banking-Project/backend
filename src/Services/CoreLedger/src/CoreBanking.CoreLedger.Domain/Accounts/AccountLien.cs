using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreBanking.CoreLedger.Domain.Accounts;

/// <summary>
/// The approved lien priority codes with their fixed rank. Codes are registered in code, never free text.
/// Decision: P1-E04-D11.
/// </summary>
public enum LienPriorityCode
{
    StatutoryFirst = 1,
    Tax = 2,
    Institution = 3,
    Customer = 4,
}

public enum LienStatus
{
    Active,
    Released,
}

public enum LienMovementType
{
    PartialRelease,
    FullRelease,
    Satisfaction,
}

/// <summary>
/// A lien claim. The claimed amount is immutable and outstandingness is tracked separately, so a partial
/// release is an entry and never a new lien or an edit. Decision: P1-E04-D11.
/// </summary>
public sealed record AccountLien(
    string LienId,
    LienPriorityCode PriorityCode,
    int PriorityRank,
    decimal Amount,
    decimal OutstandingAmount,
    LienStatus Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo)
{
    public bool RequiresSecondApprover =>
        PriorityCode is LienPriorityCode.StatutoryFirst or LienPriorityCode.Tax or LienPriorityCode.Institution;

    public bool IsActiveAt(DateTimeOffset instant) =>
        Status == LienStatus.Active
        && EffectiveFrom <= instant
        && (EffectiveTo is null || instant < EffectiveTo);

    /// <summary>
    /// Deterministic ordering: code rank, then the declared rank, then the effective start and finally the
    /// identifier — never creation order. Decision: P1-E04-D11.
    /// </summary>
    public (int CodeRank, int PriorityRank, DateTimeOffset EffectiveFrom, string LienId) OrderKey =>
        ((int)PriorityCode, PriorityRank, EffectiveFrom, LienId);

    public static string ToToken(LienPriorityCode code) => code switch
    {
        LienPriorityCode.StatutoryFirst => "statutory_first",
        LienPriorityCode.Tax => "tax",
        LienPriorityCode.Institution => "institution",
        LienPriorityCode.Customer => "customer",
        _ => throw new ArgumentException("The lien priority code is not approved.", nameof(code)),
    };

    public static LienPriorityCode ParseCode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "statutory_first" => LienPriorityCode.StatutoryFirst,
        "tax" => LienPriorityCode.Tax,
        "institution" => LienPriorityCode.Institution,
        "customer" => LienPriorityCode.Customer,
        _ => throw new ArgumentException("The lien priority code is not approved.", nameof(value)),
    };
}

public sealed record LienMovement(
    string MovementId,
    string LienId,
    LienMovementType Type,
    decimal Amount,
    decimal OutstandingAfter,
    string ReasonCode,
    string? AuthorityReference,
    string ActorId,
    DateTimeOffset OccurredAt);

/// <summary>
/// The ledger's own availability snapshot. The account module never derives it: it reports what the ledger
/// answered and only adds its own lien total for reconciliation. Decision: P1-E04-D11.
/// </summary>
public sealed record LedgerAvailabilitySnapshot(
    decimal LedgerBalance,
    decimal Holds,
    decimal Available,
    string SourceVersion,
    DateTimeOffset AsOf);

/// <summary>The lien position handed to the ledger and reported with a lien. Advisory, never a balance.</summary>
public sealed record LienAvailabilityPosition(
    decimal TotalOutstandingLiens,
    decimal ReportedAvailable,
    decimal UncoveredLienAmount,
    bool ExceedsAvailable,
    string SourceVersion,
    DateTimeOffset AsOf);

public static class AccountLienPolicy
{
    public const string AmountInvalidCode = "account_lien_amount_invalid";
    public const string OverReleaseCode = "account_lien_over_release";
    public const string CurrencyMismatchCode = "account_lien_currency_mismatch";
    public const string AuthorityRequiredCode = "account_lien_authority_required";
    public const string UncoveredCode = "account_lien_uncovered";

    /// <summary>Creation validation. A statutory or tax lien always needs an authority reference.</summary>
    public static AccountLien Create(
        string lienId,
        LienPriorityCode priorityCode,
        int priorityRank,
        decimal amount,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lienId);
        if (!Enum.IsDefined(priorityCode))
        {
            throw new ArgumentException("The lien priority code is not approved.", nameof(priorityCode));
        }

        if (priorityRank < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priorityRank), "A lien priority rank cannot be negative.");
        }

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), AmountInvalidCode);
        }

        if (effectiveTo is not null && effectiveTo <= effectiveFrom)
        {
            throw new ArgumentException("The lien effective period must be half-open and non-empty.", nameof(effectiveTo));
        }

        return new AccountLien(
            lienId.Trim(),
            priorityCode,
            priorityRank,
            amount,
            amount,
            LienStatus.Active,
            effectiveFrom,
            effectiveTo);
    }

    /// <summary>
    /// A partial release reduces only the outstanding amount and keeps the lien active; the claimed amount is
    /// never edited. Decision: P1-E04-D11.
    /// </summary>
    public static AccountLien PartialRelease(AccountLien lien, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(lien);
        if (lien.Status != LienStatus.Active)
        {
            throw new InvalidOperationException("account_lien_not_active");
        }

        if (amount <= 0m || amount > lien.OutstandingAmount)
        {
            throw new InvalidOperationException(OverReleaseCode);
        }

        decimal outstanding = lien.OutstandingAmount - amount;
        return lien with
        {
            OutstandingAmount = outstanding,
            Status = outstanding == 0m ? LienStatus.Released : LienStatus.Active,
        };
    }

    /// <summary>A full release closes the lien with a final entry; history is retained.</summary>
    public static AccountLien FullRelease(AccountLien lien)
    {
        ArgumentNullException.ThrowIfNull(lien);
        if (lien.Status != LienStatus.Active)
        {
            throw new InvalidOperationException("account_lien_not_active");
        }

        return lien with { OutstandingAmount = 0m, Status = LienStatus.Released };
    }

    /// <summary>A ledger settlement consumes outstanding amount; the account module never infers it.</summary>
    public static AccountLien Satisfy(AccountLien lien, decimal amount) => PartialRelease(lien, amount);

    /// <summary>Active liens ordered by the approved key, never by creation order.</summary>
    public static IReadOnlyList<AccountLien> Order(IEnumerable<AccountLien> liens) =>
        liens
            .OrderBy(lien => lien.OrderKey.CodeRank)
            .ThenBy(lien => lien.OrderKey.PriorityRank)
            .ThenBy(lien => lien.OrderKey.EffectiveFrom)
            .ThenBy(lien => lien.OrderKey.LienId, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Combines the ledger's answer with this module's lien total. The reported available balance always comes
    /// from the ledger; only the shortfall is computed here. Decision: P1-E04-D11.
    /// </summary>
    public static LienAvailabilityPosition Combine(
        LedgerAvailabilitySnapshot snapshot,
        IEnumerable<AccountLien> liens)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(liens);

        decimal total = liens
            .Where(lien => lien.Status == LienStatus.Active)
            .Sum(lien => lien.OutstandingAmount);
        decimal capacity = snapshot.LedgerBalance - snapshot.Holds;
        decimal uncovered = Math.Max(0m, total - Math.Max(0m, capacity));
        return new LienAvailabilityPosition(
            total,
            snapshot.Available,
            uncovered,
            uncovered > 0m,
            snapshot.SourceVersion,
            snapshot.AsOf);
    }
}
