using System;

namespace CoreBanking.CoreLedger.Domain.Ledger;

public enum LedgerHoldStatus
{
    Active = 1,
    PartiallyCaptured = 2,
    Captured = 3,
    Released = 4,
    Expired = 5,
}

/// <summary>
/// A temporary reservation. It changes available funds but never ledger balance; capture links the
/// reservation to a separately balanced posting. Decision: P1-E05-D06.
/// </summary>
public sealed class LedgerHold
{
    private LedgerHold(
        string holdId,
        string ledgerAccountId,
        Money originalAmount,
        decimal remainingAmount,
        decimal capturedAmount,
        decimal releasedAmount,
        LedgerHoldStatus status,
        DateTimeOffset expiresAt,
        long version)
    {
        HoldId = Required(holdId, nameof(holdId));
        LedgerAccountId = Required(ledgerAccountId, nameof(ledgerAccountId));
        OriginalAmount = originalAmount;
        RemainingAmount = Money.Create(remainingAmount, originalAmount.Currency.Value).Amount;
        CapturedAmount = Money.Create(capturedAmount, originalAmount.Currency.Value).Amount;
        ReleasedAmount = Money.Create(releasedAmount, originalAmount.Currency.Value).Amount;
        Status = status;
        ExpiresAt = expiresAt;
        Version = version > 0 ? version : throw new LedgerPostingException("ledger_hold_version_invalid");
        EnsureInvariant();
    }

    public string HoldId { get; }
    public string LedgerAccountId { get; }
    public Money OriginalAmount { get; }
    public decimal RemainingAmount { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal ReleasedAmount { get; private set; }
    public LedgerHoldStatus Status { get; private set; }
    public DateTimeOffset ExpiresAt { get; }
    public long Version { get; private set; }

    public static LedgerHold Create(
        string holdId,
        string ledgerAccountId,
        Money amount,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt)
    {
        if (expiresAt <= createdAt)
        {
            throw new LedgerPostingException("ledger_hold_expiry_invalid");
        }

        return new LedgerHold(
            holdId,
            ledgerAccountId,
            amount,
            amount.Amount,
            0m,
            0m,
            LedgerHoldStatus.Active,
            expiresAt,
            1);
    }

    public static LedgerHold Restore(
        string holdId,
        string ledgerAccountId,
        Money originalAmount,
        decimal remainingAmount,
        decimal capturedAmount,
        decimal releasedAmount,
        LedgerHoldStatus status,
        DateTimeOffset expiresAt,
        long version) =>
        new(
            holdId,
            ledgerAccountId,
            originalAmount,
            remainingAmount,
            capturedAmount,
            releasedAmount,
            status,
            expiresAt,
            version);

    public void Capture(decimal amount, DateTimeOffset capturedAt)
    {
        EnsureActive(capturedAt);
        decimal value = Positive(amount);
        if (value > RemainingAmount)
        {
            throw new LedgerPostingException("ledger_hold_capture_exceeds_remaining");
        }

        RemainingAmount -= value;
        CapturedAmount += value;
        Status = RemainingAmount == 0m ? LedgerHoldStatus.Captured : LedgerHoldStatus.PartiallyCaptured;
        Version++;
        EnsureInvariant();
    }

    public void Release(decimal amount, DateTimeOffset releasedAt)
    {
        EnsureActive(releasedAt);
        decimal value = Positive(amount);
        if (value > RemainingAmount)
        {
            throw new LedgerPostingException("ledger_hold_release_exceeds_remaining");
        }

        RemainingAmount -= value;
        ReleasedAmount += value;
        if (RemainingAmount == 0m)
        {
            Status = LedgerHoldStatus.Released;
        }

        Version++;
        EnsureInvariant();
    }

    public decimal Expire(DateTimeOffset expiredAt)
    {
        if (expiredAt < ExpiresAt)
        {
            throw new LedgerPostingException("ledger_hold_not_expired");
        }

        if (Status is LedgerHoldStatus.Captured or LedgerHoldStatus.Released or LedgerHoldStatus.Expired)
        {
            return 0m;
        }

        decimal released = RemainingAmount;
        RemainingAmount = 0m;
        ReleasedAmount += released;
        Status = LedgerHoldStatus.Expired;
        Version++;
        EnsureInvariant();
        return released;
    }

    private void EnsureActive(DateTimeOffset instant)
    {
        if (instant >= ExpiresAt)
        {
            throw new LedgerPostingException("ledger_hold_expired");
        }

        if (Status is not (LedgerHoldStatus.Active or LedgerHoldStatus.PartiallyCaptured))
        {
            throw new LedgerPostingException("ledger_hold_not_active");
        }
    }

    private void EnsureInvariant()
    {
        if (RemainingAmount < 0m || CapturedAmount < 0m || ReleasedAmount < 0m
            || RemainingAmount + CapturedAmount + ReleasedAmount != OriginalAmount.Amount)
        {
            throw new LedgerPostingException("ledger_hold_amounts_invalid");
        }
    }

    private static decimal Positive(decimal value) => value > 0m
        ? value
        : throw new LedgerPostingException("ledger_hold_amount_invalid");

    private static string Required(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }
}
