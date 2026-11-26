using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace CoreBanking.CoreLedger.Domain.Ledger;

public enum ManualJournalStatus
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    Posted,
}

/// <summary>One resolved manual-journal line. Amounts are positive; direction carries the accounting sign.</summary>
public sealed record ManualJournalLine(
    int Sequence,
    string LedgerAccountId,
    string ChartAccountCode,
    LedgerAccountRole Role,
    string AccountClass,
    DebitCreditIndicator Direction,
    decimal Amount,
    AccountingComponentType Component);

/// <summary>One recorded Workflow approval of the exact submitted content. Decision: P1-E05-D11.</summary>
public sealed record ManualJournalApproval(
    string DecisionId,
    string ApproverActorId,
    string Permission,
    bool IsFinanceHead,
    DateTimeOffset DecidedAt);

/// <summary>
/// The immutable submitted content of one manual journal and its exact hash. It is the only Phase 1 journal path
/// that names ledger accounts directly, so every line is validated against the approved class grants.
/// Decision: P1-E05-D11.
/// </summary>
public sealed class ManualJournalContent
{
    private ManualJournalContent(
        string manualJournalId,
        string requestId,
        string institutionId,
        string legalEntityCode,
        CurrencyCode currency,
        string reasonCode,
        string evidenceReference,
        string makerActorId,
        IReadOnlyList<ManualJournalLine> lines,
        string requestHash,
        decimal totalAmount)
    {
        ManualJournalId = manualJournalId;
        RequestId = requestId;
        InstitutionId = institutionId;
        LegalEntityCode = legalEntityCode;
        Currency = currency;
        ReasonCode = reasonCode;
        EvidenceReference = evidenceReference;
        MakerActorId = makerActorId;
        Lines = lines;
        RequestHash = requestHash;
        TotalAmount = totalAmount;
    }

    public string ManualJournalId { get; }
    public string RequestId { get; }
    public string InstitutionId { get; }
    public string LegalEntityCode { get; }
    public CurrencyCode Currency { get; }
    public string ReasonCode { get; }
    public string EvidenceReference { get; }
    public string MakerActorId { get; }
    public IReadOnlyList<ManualJournalLine> Lines { get; }
    public string RequestHash { get; }
    public decimal TotalAmount { get; }

    public string[] AccountClasses => Lines.Select(line => line.AccountClass).Distinct(StringComparer.Ordinal).ToArray();

    public static ManualJournalContent Create(
        ManualJournalPolicy policy,
        string manualJournalId,
        string requestId,
        string institutionId,
        string legalEntityCode,
        string currencyCode,
        string reasonCode,
        string evidenceReference,
        string makerActorId,
        IEnumerable<ManualJournalLine> lines)
    {
        ArgumentNullException.ThrowIfNull(policy);
        string id = Required(manualJournalId, nameof(manualJournalId), 128);
        string request = Required(requestId, nameof(requestId), 128);
        string institution = Required(institutionId, nameof(institutionId), 128);
        string legalEntity = Required(legalEntityCode, nameof(legalEntityCode), 64);
        CurrencyCode currency = CurrencyCode.Parse(currencyCode);
        string reason = Required(reasonCode, nameof(reasonCode), 64);
        string evidence = Required(evidenceReference, nameof(evidenceReference), 256);
        string maker = Required(makerActorId, nameof(makerActorId), 128);

        if (!policy.AcceptsReason(reason))
        {
            throw new LedgerPostingException("manual_journal_reason_not_approved");
        }

        ManualJournalLine[] materialised = (lines ?? throw new ArgumentNullException(nameof(lines))).ToArray();
        if (materialised.Length < 2)
        {
            throw new LedgerPostingException("manual_journal_lines_insufficient");
        }

        if (materialised.Select(line => line.Sequence).Distinct().Count() != materialised.Length
            || !materialised.Select(line => line.Sequence).Order().SequenceEqual(Enumerable.Range(1, materialised.Length)))
        {
            throw new LedgerPostingException("manual_journal_line_identity_conflict");
        }

        foreach (ManualJournalLine line in materialised)
        {
            if (line.Amount <= 0m)
            {
                throw new LedgerPostingException("manual_journal_line_amount_invalid");
            }

            if (line.Component != AccountingComponentType.Principal && string.IsNullOrWhiteSpace(reason))
            {
                throw new LedgerPostingException("accounting_component_reason_required");
            }

            ManualJournalClassGrant grant = policy.Classes.SingleOrDefault(item => item.Code == line.AccountClass)
                ?? throw new LedgerPostingException("manual_journal_class_not_approved");
            if (!grant.Roles.Contains(line.Role))
            {
                throw new LedgerPostingException("manual_journal_class_role_mismatch");
            }
        }

        foreach (ManualJournalClassGrant compensatingGrant in policy.Classes.Where(grant => grant.RequiresCompensatingAccount))
        {
            bool carriesClass = materialised.Any(line => line.AccountClass == compensatingGrant.Code);
            bool carriesCompensatingLine = materialised.Any(line =>
                line.AccountClass != compensatingGrant.Code && !compensatingGrant.Roles.Contains(line.Role));
            if (carriesClass && !carriesCompensatingLine)
            {
                throw new LedgerPostingException("manual_journal_compensating_account_required");
            }
        }

        decimal debits = materialised.Where(line => line.Direction == DebitCreditIndicator.Debit).Sum(line => line.Amount);
        decimal credits = materialised.Where(line => line.Direction == DebitCreditIndicator.Credit).Sum(line => line.Amount);
        if (debits <= 0m || debits != credits)
        {
            throw new LedgerPostingException("journal_unbalanced");
        }

        string hash = ComputeHash(
            institution,
            id,
            request,
            legalEntity,
            currency.Value,
            reason,
            evidence,
            maker,
            materialised);

        return new ManualJournalContent(
            id,
            request,
            institution,
            legalEntity,
            currency,
            reason,
            evidence,
            maker,
            Array.AsReadOnly(materialised),
            hash,
            debits);
    }

    public static string ComputeHash(
        string institutionId,
        string manualJournalId,
        string requestId,
        string legalEntityCode,
        string currencyCode,
        string reasonCode,
        string evidenceReference,
        string makerActorId,
        IReadOnlyList<ManualJournalLine> lines)
    {
        string json = JsonSerializer.Serialize(new
        {
            institutionId,
            manualJournalId,
            requestId,
            legalEntityCode,
            currencyCode,
            reasonCode,
            evidenceReference,
            makerActorId,
            lines = lines
                .OrderBy(line => line.Sequence)
                .Select(line => new
                {
                    line.Sequence,
                    line.LedgerAccountId,
                    line.ChartAccountCode,
                    Role = line.Role.ToString(),
                    line.AccountClass,
                    Direction = line.Direction.ToString(),
                    Amount = line.Amount.ToString("0.####", CultureInfo.InvariantCulture),
                    Component = line.Component.ToString(),
                })
                .ToArray(),
        });
        return CanonicalFinancialRequest.Create(json).Hash;
    }

    private static string Required(string value, string name, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        string candidate = value.Trim();
        if (candidate.Length > maximumLength)
        {
            throw new ArgumentException($"{name} may contain at most {maximumLength} characters.", name);
        }

        return candidate;
    }
}

/// <summary>
/// The approved manual-journal transitions. A request is submitted once, decided by at least the approved quorum,
/// and posted once; nothing rewrites the submitted content. Decision: P1-E05-D11.
/// </summary>
public static class ManualJournalRules
{
    public static void EnsureSubmittable(ManualJournalStatus status)
    {
        if (status != ManualJournalStatus.Draft)
        {
            throw new LedgerPostingException("manual_journal_state_invalid");
        }
    }

    public static void EnsureDecidable(ManualJournalStatus status)
    {
        if (status is not (ManualJournalStatus.Submitted or ManualJournalStatus.Approved))
        {
            throw new LedgerPostingException("manual_journal_state_invalid");
        }
    }

    public static void EnsureBreakGlassPostable(ManualJournalStatus status)
    {
        if (status != ManualJournalStatus.Draft)
        {
            throw new LedgerPostingException("manual_journal_state_invalid");
        }
    }

    public static void EnsureApproverIsNotMaker(string approverActorId, string makerActorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approverActorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(makerActorId);
        if (string.Equals(approverActorId.Trim(), makerActorId.Trim(), StringComparison.Ordinal))
        {
            throw new LedgerPostingException("manual_journal_self_approval_denied");
        }
    }
}
