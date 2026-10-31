using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CustomerOnboarding.Application.Audit;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Audit;

/// <summary>
/// Read-only integrity reconciliation across every institution. Each check is a deterministic invariant that must
/// always hold, and a finding is reported, never repaired. Decision: P1-E03-D14.
/// </summary>
public sealed class CustomerReconciliationService : ICustomerReconciliationService
{
    public const string MergeAwaitingConsumers = "merge_awaiting_consumers";
    public const string MergeAppliedWithoutAlias = "merge_applied_without_alias";
    public const string TransitionWithoutApproval = "transition_without_approval";
    public const string KycRequirementWithoutVerification = "kyc_requirement_without_verification";
    public const string AliasTargetMissing = "alias_target_missing";

    private static readonly string[] AppliedMergeStates = ["applying", "reconciliation_required", "completed"];
    private static readonly string[] ApprovalRequiredTargets = ["restricted", "deceased", "closed"];
    private static readonly string[] VerificationDependentRequirementStates = ["in_progress", "passed", "inconclusive", "manual_review"];

    private readonly CustomerOnboardingDbContext _context;

    public CustomerReconciliationService(CustomerOnboardingDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<CustomerReconciliationReport> ReconcileAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<CustomerReconciliationFinding> findings = [];

        findings.AddRange(await MergeAwaitingConsumersAsync(cancellationToken).ConfigureAwait(false));
        findings.AddRange(await MergeAppliedWithoutAliasAsync(cancellationToken).ConfigureAwait(false));
        findings.AddRange(await TransitionsWithoutApprovalAsync(cancellationToken).ConfigureAwait(false));
        findings.AddRange(await RequirementsWithoutVerificationAsync(cancellationToken).ConfigureAwait(false));
        findings.AddRange(await AliasesWithMissingSurvivorAsync(cancellationToken).ConfigureAwait(false));

        Dictionary<string, int> byCode = findings
            .GroupBy(finding => finding.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return new CustomerReconciliationReport(now, findings.Count, byCode, findings);
    }

    private async Task<List<CustomerReconciliationFinding>> MergeAwaitingConsumersAsync(CancellationToken cancellationToken)
    {
        List<(string InstitutionId, string MergeRequestId)> rows = await _context.MergeRequests
            .IgnoreQueryFilters()
            .Where(request => request.Status == "reconciliation_required")
            .Select(request => new ValueTuple<string, string>(request.InstitutionId, request.MergeRequestId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new CustomerReconciliationFinding(
                MergeAwaitingConsumers,
                row.InstitutionId,
                "merge_request",
                row.MergeRequestId)),
        ];
    }

    private async Task<List<CustomerReconciliationFinding>> MergeAppliedWithoutAliasAsync(CancellationToken cancellationToken)
    {
        List<(string InstitutionId, string MergeRequestId)> rows = await _context.MergeRequests
            .IgnoreQueryFilters()
            .Where(request => AppliedMergeStates.Contains(request.Status))
            .Where(request => !_context.CustomerAliases
                .IgnoreQueryFilters()
                .Any(alias => alias.InstitutionId == request.InstitutionId
                    && alias.MergeRequestId == request.MergeRequestId))
            .Select(request => new ValueTuple<string, string>(request.InstitutionId, request.MergeRequestId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new CustomerReconciliationFinding(
                MergeAppliedWithoutAlias,
                row.InstitutionId,
                "merge_request",
                row.MergeRequestId)),
        ];
    }

    private async Task<List<CustomerReconciliationFinding>> TransitionsWithoutApprovalAsync(CancellationToken cancellationToken)
    {
        List<(string InstitutionId, string CustomerId, string Status)> rows = await _context.CustomerStatusHistory
            .IgnoreQueryFilters()
            .Where(history => ApprovalRequiredTargets.Contains(history.ToStatus) && history.ApprovalReference == null)
            .Select(history => new ValueTuple<string, string, string>(history.InstitutionId, history.CustomerId, history.ToStatus))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new CustomerReconciliationFinding(
                TransitionWithoutApproval,
                row.InstitutionId,
                "customer_status_history:" + row.Status,
                row.CustomerId)),
        ];
    }

    private async Task<List<CustomerReconciliationFinding>> RequirementsWithoutVerificationAsync(CancellationToken cancellationToken)
    {
        List<(string InstitutionId, string CaseId, string RequirementCode)> rows = await _context.KycRequirements
            .IgnoreQueryFilters()
            .Where(requirement => VerificationDependentRequirementStates.Contains(requirement.Status))
            .Where(requirement => requirement.LinkedVerificationId == null
                || !_context.IdentityVerifications
                    .IgnoreQueryFilters()
                    .Any(verification => verification.InstitutionId == requirement.InstitutionId
                        && verification.VerificationId == requirement.LinkedVerificationId))
            .Select(requirement => new ValueTuple<string, string, string>(
                requirement.InstitutionId,
                requirement.CaseId,
                requirement.RequirementCode))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new CustomerReconciliationFinding(
                KycRequirementWithoutVerification,
                row.InstitutionId,
                "kyc_requirement",
                row.CaseId + ":" + row.RequirementCode)),
        ];
    }

    private async Task<List<CustomerReconciliationFinding>> AliasesWithMissingSurvivorAsync(CancellationToken cancellationToken)
    {
        List<(string InstitutionId, string AliasCustomerId)> rows = await _context.CustomerAliases
            .IgnoreQueryFilters()
            .Where(alias => !_context.Customers
                .IgnoreQueryFilters()
                .Any(customer => customer.InstitutionId == alias.InstitutionId
                    && customer.CustomerId == alias.SurvivorCustomerId))
            .Select(alias => new ValueTuple<string, string>(alias.InstitutionId, alias.AliasCustomerId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new CustomerReconciliationFinding(
                AliasTargetMissing,
                row.InstitutionId,
                "customer_alias",
                row.AliasCustomerId)),
        ];
    }
}
