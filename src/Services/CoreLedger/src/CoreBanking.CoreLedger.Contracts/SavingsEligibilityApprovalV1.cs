using System;
using System.Security.Cryptography;
using System.Text;

namespace CoreBanking.CoreLedger.Contracts;

public static class SavingsEligibilityApprovalV1
{
    public const string WorkflowKey = "product.savings.eligibility.publish.v1";
    public const string WorkflowVersionId = "savings-eligibility-publication-v1";
    public const string SubjectType = "product.savings.eligibility.version";
    public const string SourceService = "core-ledger";
    public const string RequiredPermission = "product.version.approve";

    public static string WorkflowDefinitionHash { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{WorkflowKey}|{WorkflowVersionId}|one-level|maker-excluded|{RequiredPermission}|decision-time-entitlement")))
        .ToLowerInvariant();
}

public sealed record ApprovalRequestedV1(
    string InstitutionId,
    string RequestId,
    string SourceRequestKey,
    string WorkflowKey,
    string WorkflowVersionId,
    string WorkflowDefinitionHash,
    string SourceService,
    string SubjectType,
    string SubjectId,
    string SubjectVersion,
    string SubjectHash,
    string MakerActorId,
    string RequiredPermission,
    string CorrelationId,
    DateTimeOffset SubmittedAt);

public sealed record ApprovalDecidedV1(
    string InstitutionId,
    string RequestId,
    string DecisionId,
    string Decision,
    string WorkflowKey,
    string WorkflowVersionId,
    string WorkflowDefinitionHash,
    string SubjectType,
    string SubjectId,
    string SubjectVersion,
    string SubjectHash,
    string ApproverActorId,
    string Permission,
    string EntitlementVersion,
    DateTimeOffset EntitlementValidFrom,
    DateTimeOffset EntitlementValidUntil,
    string? Reason,
    string CorrelationId,
    DateTimeOffset DecidedAt);
