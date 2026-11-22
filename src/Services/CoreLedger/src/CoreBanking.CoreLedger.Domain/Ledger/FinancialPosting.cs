using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CoreBanking.CoreLedger.Domain.Ledger;

public enum FinancialTransactionStatus
{
    Received,
    PendingAuthorisation,
    ReadyToPost,
    Posted,
    Rejected,
    FailedBeforeCommit,
    Disputed,
    Reversed,
}

/// <summary>
/// One immutable line intent. Amounts are always positive; direction carries the accounting sign.
/// Decision: P1-E05-D05.
/// </summary>
public sealed record JournalLine
{
    private JournalLine(
        string lineId,
        int sequence,
        string ledgerAccountId,
        DebitCreditIndicator direction,
        AccountingComponentType component,
        Money amount,
        AccountingScope scope,
        string? reasonCode)
    {
        LineId = lineId;
        Sequence = sequence;
        LedgerAccountId = ledgerAccountId;
        Direction = direction;
        Component = component;
        Amount = amount;
        Scope = scope;
        ReasonCode = reasonCode;
    }

    public string LineId { get; }
    public int Sequence { get; }
    public string LedgerAccountId { get; }
    public DebitCreditIndicator Direction { get; }
    public AccountingComponentType Component { get; }
    public Money Amount { get; }
    public AccountingScope Scope { get; }
    public string? ReasonCode { get; }

    public static JournalLine Create(
        string lineId,
        int sequence,
        string ledgerAccountId,
        DebitCreditIndicator direction,
        AccountingComponentType component,
        Money amount,
        AccountingScope scope,
        string? reasonCode = null)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(scope);

        if (sequence < 1)
        {
            throw new LedgerPostingException("journal_line_sequence_invalid");
        }

        if (amount.Amount <= 0)
        {
            throw new LedgerPostingException("journal_line_amount_invalid");
        }

        if (amount.Currency != scope.Currency)
        {
            throw new LedgerPostingException("journal_line_currency_mismatch");
        }

        string? normalisedReason = string.IsNullOrWhiteSpace(reasonCode) ? null : Required(reasonCode, nameof(reasonCode));
        if (component != AccountingComponentType.Principal && normalisedReason is null)
        {
            throw new LedgerPostingException("accounting_component_reason_required");
        }

        return new JournalLine(
            Required(lineId, nameof(lineId)),
            sequence,
            Required(ledgerAccountId, nameof(ledgerAccountId)),
            direction,
            component,
            amount,
            scope,
            normalisedReason);
    }

    private static string Required(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }
}

/// <summary>A complete immutable posted journal. Construction is the domain balance gate.</summary>
public sealed class JournalEntry
{
    private readonly ReadOnlyCollection<JournalLine> _lines;

    private JournalEntry(
        string journalEntryId,
        string financialTransactionId,
        FinancialReference journalReference,
        string mappingVersionId,
        AccountingScope scope,
        FinancialDates dates,
        JournalLine[] lines)
    {
        JournalEntryId = journalEntryId;
        FinancialTransactionId = financialTransactionId;
        JournalReference = journalReference;
        MappingVersionId = mappingVersionId;
        Scope = scope;
        Dates = dates;
        _lines = Array.AsReadOnly(lines);
        TotalDebits = lines.Where(line => line.Direction == DebitCreditIndicator.Debit).Sum(line => line.Amount.Amount);
        TotalCredits = lines.Where(line => line.Direction == DebitCreditIndicator.Credit).Sum(line => line.Amount.Amount);
    }

    public string JournalEntryId { get; }
    public string FinancialTransactionId { get; }
    public FinancialReference JournalReference { get; }
    public string MappingVersionId { get; }
    public AccountingScope Scope { get; }
    public FinancialDates Dates { get; }
    public IReadOnlyList<JournalLine> Lines => _lines;
    public decimal TotalDebits { get; }
    public decimal TotalCredits { get; }

    public static JournalEntry Create(
        string journalEntryId,
        string financialTransactionId,
        string journalReference,
        string mappingVersionId,
        AccountingScope scope,
        FinancialDates dates,
        IEnumerable<JournalLine> lines)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(dates);
        JournalLine[] materialised = lines?.ToArray() ?? throw new ArgumentNullException(nameof(lines));

        if (materialised.Length < 2)
        {
            throw new LedgerPostingException("journal_lines_insufficient");
        }

        if (materialised.Select(line => line.LineId).Distinct(StringComparer.Ordinal).Count() != materialised.Length
            || materialised.Select(line => line.Sequence).Distinct().Count() != materialised.Length
            || !materialised.Select(line => line.Sequence).Order().SequenceEqual(Enumerable.Range(1, materialised.Length)))
        {
            throw new LedgerPostingException("journal_line_identity_conflict");
        }

        if (materialised.Any(line => line.Scope != scope))
        {
            throw new LedgerPostingException("accounting_scope_mismatch");
        }

        decimal debits = materialised.Where(line => line.Direction == DebitCreditIndicator.Debit).Sum(line => line.Amount.Amount);
        decimal credits = materialised.Where(line => line.Direction == DebitCreditIndicator.Credit).Sum(line => line.Amount.Amount);
        if (debits != credits)
        {
            throw new LedgerPostingException("journal_unbalanced");
        }

        return new JournalEntry(
            Required(journalEntryId, nameof(journalEntryId)),
            Required(financialTransactionId, nameof(financialTransactionId)),
            FinancialReference.Parse(journalReference),
            Required(mappingVersionId, nameof(mappingVersionId)),
            scope,
            dates,
            materialised);
    }

    private static string Required(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }
}

/// <summary>
/// Trace root for a financial posting. This slice deliberately exposes no persistence or transport use case.
/// </summary>
public sealed class FinancialTransaction
{
    private IReadOnlyList<JournalEntry> _journals = Array.Empty<JournalEntry>();

    private FinancialTransaction(
        string id,
        FinancialReference transactionReference,
        string institutionId,
        string sourceService,
        FinancialReference sourceReference,
        string eventCode,
        string requestHash,
        FinancialDates dates)
    {
        Id = id;
        TransactionReference = transactionReference;
        InstitutionId = institutionId;
        SourceService = sourceService;
        SourceReference = sourceReference;
        EventCode = eventCode;
        RequestHash = requestHash;
        Dates = dates;
        Status = FinancialTransactionStatus.ReadyToPost;
    }

    public string Id { get; }
    public FinancialReference TransactionReference { get; }
    public string InstitutionId { get; }
    public string SourceService { get; }
    public FinancialReference SourceReference { get; }
    public string EventCode { get; }
    public string RequestHash { get; }
    public FinancialDates Dates { get; }
    public FinancialTransactionStatus Status { get; private set; }
    public IReadOnlyList<JournalEntry> Journals => _journals;

    public static FinancialTransaction CreateReadyToPost(
        string id,
        string transactionReference,
        string institutionId,
        string sourceService,
        string sourceReference,
        string eventCode,
        string requestHash,
        FinancialDates dates)
    {
        ArgumentNullException.ThrowIfNull(dates);
        return new FinancialTransaction(
            Required(id, nameof(id)),
            FinancialReference.Parse(transactionReference),
            Required(institutionId, nameof(institutionId)),
            Required(sourceService, nameof(sourceService)),
            FinancialReference.Parse(sourceReference),
            Required(eventCode, nameof(eventCode)),
            RequiredHash(requestHash),
            dates);
    }

    public void MarkPosted(IEnumerable<JournalEntry> journals)
    {
        if (Status != FinancialTransactionStatus.ReadyToPost)
        {
            throw new LedgerPostingException("financial_transaction_state_invalid");
        }

        JournalEntry[] materialised = journals?.ToArray() ?? throw new ArgumentNullException(nameof(journals));
        if (materialised.Length == 0)
        {
            throw new LedgerPostingException("financial_transaction_journal_required");
        }

        if (materialised.Any(journal => !string.Equals(journal.FinancialTransactionId, Id, StringComparison.Ordinal))
            || materialised.Select(journal => journal.JournalEntryId).Distinct(StringComparer.Ordinal).Count() != materialised.Length
            || materialised.Select(journal => journal.JournalReference.Value).Distinct(StringComparer.Ordinal).Count() != materialised.Length)
        {
            throw new LedgerPostingException("financial_transaction_journal_mismatch");
        }

        _journals = Array.AsReadOnly(materialised);
        Status = FinancialTransactionStatus.Posted;
    }

    private static string Required(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }

    private static string RequiredHash(string value)
    {
        string candidate = Required(value, nameof(value));
        if (candidate.Length != 64 || candidate.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Request hash must be a SHA-256 hexadecimal digest.", nameof(value));
        }

        return candidate.ToLowerInvariant();
    }
}

public sealed class LedgerPostingException : InvalidOperationException
{
    public LedgerPostingException(string code)
        : base(code) => Code = code;

    public string Code { get; }
}
