using System;
using System.Security.Cryptography;
using System.Text;

namespace CoreBanking.CoreLedger.Contracts;

/// <summary>
/// The approved approval profile for institution configuration publication. The identifiers bind the
/// source request to the Operations and Workflow profile; the permission is the one Epic 1 assigns to
/// the Configuration Approver role. Decision: P1-E04-D17, Epic 1 section 5.
/// </summary>
public static class InstitutionConfigurationApprovalV1
{
    public const string WorkflowKey = "institution.configuration.publish.v1";
    public const string WorkflowVersionId = "institution-configuration-publication-v1";
    public const string SubjectType = "institution.configuration.version";
    public const string SourceService = "core-ledger";
    public const string RequiredPermission = "institution.configuration.approve";

    public static string WorkflowDefinitionHash { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{WorkflowKey}|{WorkflowVersionId}|one-level|maker-excluded|{RequiredPermission}|decision-time-entitlement")))
        .ToLowerInvariant();
}
