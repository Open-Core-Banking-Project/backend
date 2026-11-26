using System;

namespace CoreBanking.CoreLedger.Domain.Ledger;

/// <summary>
/// Authoritative accumulated position for one ledger account. Debit and credit totals are stored
/// independently; direction, never the sign of an amount, changes the position. Decisions:
/// P1-E05-D05 and P1-E05-D06.
/// </summary>
public sealed class LedgerBalance
{
    private LedgerBalance(
        string ledgerAccountId,
        CurrencyCode currency,
        DebitCreditIndicator normalSide,
        decimal debitTotal,
        decimal creditTotal,
        decimal creditLimit,
        decimal holdRemaining,
        decimal pendingAuthorised,
        long version)
    {
        LedgerAccountId = Required(ledgerAccountId, nameof(ledgerAccountId));
        Currency = currency;
        NormalSide = normalSide;
        DebitTotal = Money.Create(debitTotal, currency.Value).Amount;
        CreditTotal = Money.Create(creditTotal, currency.Value).Amount;
        CreditLimit = Money.Create(creditLimit, currency.Value).Amount;
        HoldRemaining = Money.Create(holdRemaining, currency.Value).Amount;
        PendingAuthorised = Money.Create(pendingAuthorised, currency.Value).Amount;
        Version = version > 0 ? version : throw new LedgerPostingException("ledger_balance_version_invalid");
    }

    public string LedgerAccountId { get; }
    public CurrencyCode Currency { get; }
    public DebitCreditIndicator NormalSide { get; }
    public decimal DebitTotal { get; private set; }
    public decimal CreditTotal { get; private set; }
    public decimal CreditLimit { get; }
    public decimal HoldRemaining { get; private set; }
    public decimal PendingAuthorised { get; }
    public long Version { get; private set; }
    public decimal NetBalance => DebitTotal - CreditTotal;
    public decimal VisibleBalance => NormalSide == DebitCreditIndicator.Debit ? NetBalance : -NetBalance;

    public static LedgerBalance Restore(
        string ledgerAccountId,
        string currency,
        DebitCreditIndicator normalSide,
        decimal debitTotal,
        decimal creditTotal,
        decimal creditLimit,
        decimal holdRemaining,
        decimal pendingAuthorised,
        long version)
    {
        if (debitTotal < 0 || creditTotal < 0 || creditLimit < 0 || holdRemaining < 0 || pendingAuthorised < 0)
        {
            throw new LedgerPostingException("ledger_balance_component_invalid");
        }

        return new LedgerBalance(
            ledgerAccountId,
            CurrencyCode.Parse(currency),
            normalSide,
            debitTotal,
            creditTotal,
            creditLimit,
            holdRemaining,
            pendingAuthorised,
            version);
    }

    public LedgerMovement Apply(JournalLine line, string movementId, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!string.Equals(line.LedgerAccountId, LedgerAccountId, StringComparison.Ordinal))
        {
            throw new LedgerPostingException("ledger_balance_account_mismatch");
        }

        if (line.Amount.Currency != Currency)
        {
            throw new LedgerPostingException("ledger_balance_currency_mismatch");
        }

        decimal before = VisibleBalance;
        if (line.Direction == DebitCreditIndicator.Debit)
        {
            DebitTotal += line.Amount.Amount;
        }
        else
        {
            CreditTotal += line.Amount.Amount;
        }

        Version++;
        return new LedgerMovement(
            Required(movementId, nameof(movementId)),
            line.LineId,
            LedgerAccountId,
            line.Direction,
            line.Amount,
            before,
            VisibleBalance,
            DebitTotal,
            CreditTotal,
            Version,
            occurredAt);
    }

    public AvailableFundsPosition Available(decimal outstandingLiens)
    {
        if (outstandingLiens < 0)
        {
            throw new LedgerPostingException("outstanding_lien_invalid");
        }

        decimal deductions = HoldRemaining + outstandingLiens + PendingAuthorised;
        decimal rawAvailable = VisibleBalance + CreditLimit - deductions;
        return new AvailableFundsPosition(
            Math.Max(0m, rawAvailable),
            Math.Max(0m, -rawAvailable),
            VisibleBalance,
            CreditLimit,
            HoldRemaining,
            outstandingLiens,
            PendingAuthorised);
    }

    public void ReserveHold(decimal amount, decimal outstandingLiens)
    {
        decimal value = Money.Create(amount, Currency.Value).Amount;
        if (value <= 0m)
        {
            throw new LedgerPostingException("ledger_hold_amount_invalid");
        }

        if (Available(outstandingLiens).Available < value)
        {
            throw new LedgerPostingException("insufficient_available_funds");
        }

        HoldRemaining += value;
        Version++;
    }

    public void ReduceHold(decimal amount)
    {
        decimal value = Money.Create(amount, Currency.Value).Amount;
        if (value <= 0m || value > HoldRemaining)
        {
            throw new LedgerPostingException("ledger_hold_balance_mismatch");
        }

        HoldRemaining -= value;
        Version++;
    }

    private static string Required(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }
}

public sealed record LedgerMovement(
    string MovementId,
    string JournalLineId,
    string LedgerAccountId,
    DebitCreditIndicator Direction,
    Money Amount,
    decimal VisibleBalanceBefore,
    decimal VisibleBalanceAfter,
    decimal DebitTotalAfter,
    decimal CreditTotalAfter,
    long BalanceVersion,
    DateTimeOffset OccurredAt);

public sealed record AvailableFundsPosition(
    decimal Available,
    decimal Deficit,
    decimal VisibleLedgerBalance,
    decimal CreditLimit,
    decimal HoldRemaining,
    decimal OutstandingLiens,
    decimal PendingAuthorised);
