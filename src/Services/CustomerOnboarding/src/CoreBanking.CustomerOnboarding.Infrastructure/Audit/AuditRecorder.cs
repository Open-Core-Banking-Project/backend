using System;
using System.Collections.Generic;
using System.Text.Json;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Contracts;
using CoreBanking.CustomerOnboarding.Infrastructure.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Audit;

/// <summary>
/// Writes one audit row and its SIEM event in the caller's transaction, so an action cannot commit without its
/// evidence. Detail carries stable codes and references only. Decision: P1-E03-D14.
/// </summary>
internal static class AuditRecorder
{
    public const string SubjectCustomer = "customer";
    public const string SubjectKycCase = "kyc_case";
    public const string SubjectRequirementPack = "requirement_pack";

    public const string ActionCustomerStatusChanged = "customer.status.changed";
    public const string ActionCustomerClassificationApproved = "customer.classification.approved";
    public const string ActionCustomerClassificationSubmitted = "customer.classification.submitted";
    public const string ActionCustomerClassificationRejected = "customer.classification.rejected";
    public const string ActionCustomerCorrectionRecorded = "customer.correction.recorded";
    public const string ActionCustomerMergeApplied = "customer.merge.applied";

    public const string ActionCustomerMergeSubmitted = "customer.merge.submitted";

    public const string ActionCustomerMergeApproved = "customer.merge.approved";

    public const string ActionCustomerMergeRejected = "customer.merge.rejected";
    public const string ActionCustomerPreferenceSet = "customer.preference.set";
    public const string ActionKycRequirementWaived = "kyc.requirement.waived";
    public const string ActionKycRequirementResolved = "kyc.requirement.resolved";
    public const string ActionKycRequirementReopened = "kyc.requirement.reopened";
    public const string ActionRequirementPackPublished = "requirement.pack.published";

    public const string ActionRelationshipManagerAssigned = "customer.relationship_manager.assigned";

    public static void Record(
        CustomerOnboardingDbContext context,
        InstitutionId institutionId,
        string actorId,
        string action,
        string subjectType,
        string subjectReference,
        string correlationId,
        IReadOnlyDictionary<string, string> detail,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectReference);
        ArgumentNullException.ThrowIfNull(detail);

        string auditEventId = Guid.NewGuid().ToString("N");
        string serialisedDetail = JsonSerializer.Serialize(detail);

        context.CustomerAuditEvents.Add(new CustomerAuditEventRecord
        {
            InstitutionId = institutionId.Value,
            AuditEventId = auditEventId,
            OccurredAt = now,
            ActorId = actorId ?? string.Empty,
            Action = action,
            SubjectType = subjectType,
            SubjectReference = subjectReference,
            CorrelationId = correlationId ?? string.Empty,
            Detail = serialisedDetail,
        });

        CustomerEventWriter.Add(
            context,
            institutionId,
            subjectReference,
            CustomerContractV1.AuditEventType,
            new CustomerAuditEventV1(
                auditEventId,
                institutionId.Value,
                actorId ?? string.Empty,
                action,
                subjectType,
                subjectReference,
                correlationId ?? string.Empty,
                serialisedDetail,
                now),
            correlationId ?? string.Empty,
            now,
            CustomerContractV1.AuditEventFamily);
    }
}
