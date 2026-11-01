using System;

namespace CoreBanking.CustomerOnboarding.Contracts;

/// <summary>Published when the institution relationship is created. Carries no reason text or personal data.</summary>
public sealed record CustomerCreatedV1(
    string CustomerId,
    string PartyId,
    string InstitutionId,
    string? BranchId,
    string CustomerNumber,
    string Status,
    DateTimeOffset OccurredAt);

/// <summary>
/// Published when a customer status transition is applied. Core Banking and Ledger decides what an account
/// does; this event carries statuses and references only. Decision: P1-E03-D10.
/// </summary>
public sealed record CustomerStatusChangedV1(
    string CustomerId,
    string InstitutionId,
    string PreviousStatus,
    string NewStatus,
    string? ApprovalReference,
    DateTimeOffset OccurredAt);

/// <summary>
/// Published when a merge plan is applied locally. The payload cites the plan hash so a consumer can prove
/// which plan it applied. Decision: P1-E03-D11.
/// </summary>
public sealed record CustomersMergedV1(
    string MergeRequestId,
    string SurvivorCustomerId,
    string SourceCustomerId,
    string InstitutionId,
    string PlanHash,
    string? ApprovalReference,
    DateTimeOffset OccurredAt);

/// <summary>
/// Published alongside an audit row so an independent sink keeps a copy. Carries stable codes and references
/// only. Decision: P1-E03-D14.
/// </summary>
public sealed record CustomerAuditEventV1(
    string AuditEventId,
    string InstitutionId,
    string ActorId,
    string Action,
    string SubjectType,
    string SubjectReference,
    string CorrelationId,
    string Detail,
    DateTimeOffset OccurredAt);

/// <summary>
/// Published when a relationship-manager assignment becomes effective. The payload carries the staff identity
/// reference and the effective period only. Requirement: CUS-010, CUS-026.
/// </summary>
public sealed record RelationshipManagerAssignedV1(
    string AssignmentId,
    string? PreviousAssignmentId,
    string CustomerId,
    string InstitutionId,
    string ManagerActorId,
    string? ScopeBranchId,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset OccurredAt);

/// <summary>
/// Published when an approved correction changes a protected customer profile value. It carries the affected
/// field codes, not the values. Decision: epic 03 CUS-UC-06.
/// </summary>
public sealed record CustomerProfileChangedV1(
    string CorrectionId,
    string CustomerId,
    string InstitutionId,
    string FieldCode,
    bool RequiresReverification,
    string? RequirementCode,
    string? ApprovalReference,
    DateTimeOffset OccurredAt);

/// <summary>Stable producer, family and schema values for the customer event family.</summary>
public static class CustomerContractV1
{
    public const string Producer = "customer-onboarding";

    public const string EventFamily = "customer";

    public const int SchemaVersion = 1;

    public const string CustomerCreatedEventType = "customer_created";

    public const string CustomerStatusChangedEventType = "customer_status_changed";

    public const string CustomersMergedEventType = "customers_merged";

    public const string RelationshipManagerAssignedEventType = "relationship_manager_assigned";

    public const string CustomerProfileChangedEventType = "customer_profile_changed";

    public const string AuditEventType = "customer_audit_event";

    public const string AuditEventFamily = "audit";
}
