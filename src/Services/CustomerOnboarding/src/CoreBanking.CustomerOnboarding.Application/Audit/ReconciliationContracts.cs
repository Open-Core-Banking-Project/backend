using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CoreBanking.CustomerOnboarding.Application.Audit;

/// <summary>
/// One integrity finding. Findings carry identifiers and codes only; the job never repairs state.
/// Decision: P1-E03-D14.
/// </summary>
public sealed record CustomerReconciliationFinding(
    string Code,
    string InstitutionId,
    string SubjectType,
    string SubjectReference);

/// <summary>The result of one reconciliation pass across every institution.</summary>
public sealed record CustomerReconciliationReport(
    DateTimeOffset AssembledAt,
    int TotalFindings,
    IReadOnlyDictionary<string, int> FindingsByCode,
    IReadOnlyList<CustomerReconciliationFinding> Findings)
{
    public bool IsClean => TotalFindings == 0;
}

/// <summary>
/// Deterministic integrity checks over customer evidence. Read-only and tenant-agnostic: it is an operator job,
/// not a customer request. Decision: P1-E03-D14.
/// </summary>
public interface ICustomerReconciliationService
{
    Task<CustomerReconciliationReport> ReconcileAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
