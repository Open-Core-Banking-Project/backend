using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Ledger;

/// <summary>
/// Enforces the approved date policy and the accounting-period state inside the posting transaction. The period row is
/// serialized with an advisory transaction lock, so a concurrent close and post cannot both succeed. A missing period is
/// provisioned Open on first use; Finance controls every later transition. Decisions: P1-E05-D12 and P1-E05-D13.
/// </summary>
internal static class PostingDateGuard
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static async Task<PostingDateDecision> EnsureAsync(
        CoreLedgerDbContext context,
        string institutionId,
        string legalEntityCode,
        FinancialDates dates,
        string eventCode,
        DateAuthority? authority,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentException.ThrowIfNullOrWhiteSpace(legalEntityCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventCode);

        PostingDateDecision decision = FinancialDatePolicy.Evaluate(
            dates.BusinessDate,
            dates.PostingDate,
            dates.ValueDate,
            authority);
        if (!decision.Allowed)
        {
            throw new LedgerPostingException(decision.RefusalCode!);
        }

        string periodCode = dates.PostingDate.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        await AcquirePeriodLockAsync(context, institutionId, legalEntityCode, periodCode, cancellationToken)
            .ConfigureAwait(false);

        AccountingPeriodRecord? period = await context.AccountingPeriods
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId
                    && record.LegalEntityCode == legalEntityCode
                    && record.PeriodCode == periodCode,
                cancellationToken)
            .ConfigureAwait(false);

        if (period is null)
        {
            context.AccountingPeriods.Add(new AccountingPeriodRecord
            {
                InstitutionId = institutionId,
                LegalEntityCode = legalEntityCode,
                PeriodCode = periodCode,
                Status = "open",
                AllowedEventCodesJson = "[]",
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });
            context.AccountingPeriodHistory.Add(new AccountingPeriodHistoryRecord
            {
                InstitutionId = institutionId,
                LegalEntityCode = legalEntityCode,
                PeriodCode = periodCode,
                Sequence = 1,
                FromStatus = "open",
                ToStatus = "open",
                ActorId = "core-ledger",
                ReasonCode = "provisioned_on_first_posting",
                OccurredAt = now,
            });
            return decision;
        }

        AccountingPeriodStatus status = ParseStatus(period.Status);
        if (status == AccountingPeriodStatus.Reopened
            && period.ReopenExpiresAt is not null
            && now >= period.ReopenExpiresAt.Value)
        {
            throw new LedgerPostingException("accounting_period_reopen_expired");
        }

        string[] allowed = JsonSerializer.Deserialize<string[]>(period.AllowedEventCodesJson, SerializerOptions) ?? [];
        if (!AccountingPeriodRules.IsPostable(status, allowed, eventCode))
        {
            throw new LedgerPostingException(status switch
            {
                AccountingPeriodStatus.SoftClosed => "accounting_period_soft_closed_denied",
                AccountingPeriodStatus.Reopened => "accounting_period_reopen_scope_denied",
                _ => "accounting_period_closed",
            });
        }

        return decision;
    }

    internal static async Task AcquirePeriodLockAsync(
        CoreLedgerDbContext context,
        string institutionId,
        string legalEntityCode,
        string periodCode,
        CancellationToken cancellationToken)
    {
        string key = institutionId + "|" + legalEntityCode + "|" + periodCode;
        await context.Database
            .ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({key}))",
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static AccountingPeriodStatus ParseStatus(string value) => value switch
    {
        "open" => AccountingPeriodStatus.Open,
        "soft_closed" => AccountingPeriodStatus.SoftClosed,
        "closed" => AccountingPeriodStatus.Closed,
        "locked" => AccountingPeriodStatus.Locked,
        "reopened" => AccountingPeriodStatus.Reopened,
        _ => throw new LedgerPostingException("accounting_period_status_invalid"),
    };

    internal static string StatusToken(AccountingPeriodStatus status) => status switch
    {
        AccountingPeriodStatus.Open => "open",
        AccountingPeriodStatus.SoftClosed => "soft_closed",
        AccountingPeriodStatus.Closed => "closed",
        AccountingPeriodStatus.Locked => "locked",
        AccountingPeriodStatus.Reopened => "reopened",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    internal static string[] ReadAllowList(string json) =>
        JsonSerializer.Deserialize<string[]>(json, SerializerOptions) ?? [];

    internal static string WriteAllowList(System.Collections.Generic.IEnumerable<string> codes) =>
        JsonSerializer.Serialize(codes.OrderBy(code => code, StringComparer.Ordinal).ToArray(), SerializerOptions);
}
