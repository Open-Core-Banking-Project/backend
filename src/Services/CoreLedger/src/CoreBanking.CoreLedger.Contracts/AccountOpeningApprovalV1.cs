using System;
using System.Security.Cryptography;
using System.Text;

namespace CoreBanking.CoreLedger.Contracts;

/// <summary>
/// The approved approval profile for opening an account. The maker submits an eligible application; the
/// approver decides it and the decision is bound to the exact application version and eligibility snapshot.
/// Decision: P1-E04-D09.
/// </summary>
public static class AccountOpeningApprovalV1
{
    public const string WorkflowKey = "account.open.v1";
    public const string WorkflowVersionId = "account-open-approval-v1";
    public const string SubjectType = "account.application";
    public const string SourceService = "core-ledger";
    public const string RequiredPermission = "account.application.approve";

    public static string WorkflowDefinitionHash { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{WorkflowKey}|{WorkflowVersionId}|one-level|maker-excluded|{RequiredPermission}|decision-time-entitlement")))
        .ToLowerInvariant();
}
