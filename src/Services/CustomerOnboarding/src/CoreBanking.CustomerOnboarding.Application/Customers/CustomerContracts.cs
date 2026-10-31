using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain.Customers;

namespace CoreBanking.CustomerOnboarding.Application.Customers;

/// <summary>
/// Creates the institution relationship for a submitted application's party. The customer number comes from
/// the institution sequence, never from the caller. Decision: P1-E03-D02, P1-E03-D09.
/// </summary>
public sealed record RegisterCustomerCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string ApplicationId,
    string? BranchId,
    string CorrelationId);

/// <summary>Proposes the next classification version. Values are approved configuration codes.</summary>
public sealed record ProposeClassificationCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CustomerId,
    string Tier,
    string Segment,
    string RiskClass,
    bool RequiresEnhancedDueDiligence,
    string CorrelationId);

/// <summary>Approves a proposed classification with the exact Workflow decision reference.</summary>
public sealed record ApproveClassificationCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CustomerId,
    int Version,
    string WorkflowDecisionReference,
    string CorrelationId);

/// <summary>Activates a pending customer. Requires a completed KYC case and an approved classification.</summary>
/// <summary>
/// Submits one proposed classification version to Workflow for a decision. The content hash the approver saw is
/// frozen, so a later edit cannot be approved by mistake. Decision: P1-E03-D09.
/// </summary>
public sealed record SubmitClassificationForApprovalCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CustomerId,
    int Version,
    string CorrelationId);

public sealed record ClassificationApprovalView(
    string CustomerId,
    int Version,
    string ApprovalRequestId,
    string ContentHash,
    DateTimeOffset SubmittedAt,
    bool IsReplay);

public sealed record ActivateCustomerCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CustomerId,
    int ClassificationVersion,
    string CorrelationId);

/// <summary>Applies one legal lifecycle transition with a reason and, where policy demands it, an approval reference.</summary>
public sealed record ChangeCustomerStatusCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CustomerId,
    CustomerStatus TargetStatus,
    string Reason,
    string? ApprovalReference,
    string CorrelationId);

public sealed record GetCustomerQuery(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CustomerId);

public sealed record CustomerClassificationView(
    int Version,
    string Tier,
    string Segment,
    string RiskClass,
    bool RequiresEnhancedDueDiligence,
    ClassificationStatus Status,
    string StatusCode,
    string? WorkflowDecisionReference,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset ProposedAt);

public sealed record CustomerStatusChangeView(
    int Sequence,
    CustomerStatus From,
    CustomerStatus To,
    string FromCode,
    string ToCode,
    string Reason,
    string ActorId,
    string? ApprovalReference,
    DateTimeOffset OccurredAt);

public sealed record CustomerView(
    string CustomerId,
    string PartyId,
    string CustomerNumber,
    string? BranchId,
    CustomerStatus Status,
    string StatusCode,
    int? EffectiveClassificationVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long ConcurrencyVersion,
    IReadOnlyList<CustomerClassificationView> Classifications,
    IReadOnlyList<CustomerStatusChangeView> StatusHistory);

public interface ICustomerService
{
    Task<CustomerView> RegisterAsync(RegisterCustomerCommand command, CancellationToken cancellationToken);

    Task<CustomerView> ProposeClassificationAsync(ProposeClassificationCommand command, CancellationToken cancellationToken);

    Task<CustomerView> ApproveClassificationAsync(ApproveClassificationCommand command, CancellationToken cancellationToken);

    Task<ClassificationApprovalView> SubmitClassificationForApprovalAsync(
        SubmitClassificationForApprovalCommand command,
        CancellationToken cancellationToken);

    Task<CustomerView> ActivateAsync(ActivateCustomerCommand command, CancellationToken cancellationToken);

    Task<CustomerView> ChangeStatusAsync(ChangeCustomerStatusCommand command, CancellationToken cancellationToken);

    Task<CustomerView> GetAsync(GetCustomerQuery query, CancellationToken cancellationToken);
}

/// <summary>The customer does not exist in the caller's tenant, or is not visible to the caller.</summary>
public sealed class CustomerNotFoundException : Exception
{
    public CustomerNotFoundException(string customerId)
        : base($"Customer '{customerId}' was not found.") => CustomerId = customerId;

    public string CustomerId { get; }
}
