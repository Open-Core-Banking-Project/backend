using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Domain.Customers;

namespace CoreBanking.CustomerOnboarding.Application.Customers;

/// <summary>Proposes a survivor/source merge with approved consumer codes. Decision: P1-E03-D11.</summary>
public sealed record ProposeCustomerMergeCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string SurvivorCustomerId,
    string SourceCustomerId,
    string Reason,
    IReadOnlyList<string> RequiredConsumers,
    string CorrelationId);

/// <summary>Approves a plan with the exact Workflow decision reference; the plan is frozen from here.</summary>
public sealed record ApproveCustomerMergeCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string MergeRequestId,
    string WorkflowDecisionReference,
    string CorrelationId);

/// <summary>
/// Freezes the plan and submits it to Workflow for the approval the merge needs. The merge request identifier
/// is the approval request identifier, so the loop needs no extra state. Decision: P1-E03-D11.
/// </summary>
public sealed record SubmitCustomerMergeForApprovalCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string MergeRequestId,
    string CorrelationId);

/// <summary>Applies the local part: closes the source, records the alias and publishes the merge event.</summary>
public sealed record ApplyCustomerMergeCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string MergeRequestId,
    string CorrelationId);

/// <summary>Records one consumer acknowledgement of an applying merge.</summary>
public sealed record AcknowledgeCustomerMergeCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string MergeRequestId,
    string Consumer,
    string CorrelationId);

public sealed record WithdrawCustomerMergeCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string MergeRequestId,
    string Reason,
    string CorrelationId);

public sealed record GetCustomerMergeQuery(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string MergeRequestId);

public sealed record MergeAcknowledgementView(string Consumer, DateTimeOffset AcknowledgedAt);

public sealed record CustomerMergeView(
    string MergeRequestId,
    string SurvivorCustomerId,
    string SourceCustomerId,
    string Reason,
    string PlanHash,
    MergeRequestStatus Status,
    string StatusCode,
    string? ApprovalReference,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<string> RequiredConsumers,
    IReadOnlyList<string> OutstandingConsumers,
    IReadOnlyList<MergeAcknowledgementView> Acknowledgements);

public interface ICustomerMergeService
{
    Task<CustomerMergeView> ProposeAsync(ProposeCustomerMergeCommand command, CancellationToken cancellationToken);

    Task<CustomerMergeView> ApproveAsync(ApproveCustomerMergeCommand command, CancellationToken cancellationToken);

    Task<CustomerMergeView> SubmitForApprovalAsync(
        SubmitCustomerMergeForApprovalCommand command,
        CancellationToken cancellationToken);

    Task<CustomerMergeView> ApplyAsync(ApplyCustomerMergeCommand command, CancellationToken cancellationToken);

    Task<CustomerMergeView> AcknowledgeAsync(AcknowledgeCustomerMergeCommand command, CancellationToken cancellationToken);

    Task<CustomerMergeView> WithdrawAsync(WithdrawCustomerMergeCommand command, CancellationToken cancellationToken);

    Task<CustomerMergeView> GetAsync(GetCustomerMergeQuery query, CancellationToken cancellationToken);
}

/// <summary>The merge request does not exist in the caller's tenant, or is not visible to the caller.</summary>
public sealed class CustomerMergeNotFoundException : Exception
{
    public CustomerMergeNotFoundException(string mergeRequestId)
        : base($"Customer merge request '{mergeRequestId}' was not found.") => MergeRequestId = mergeRequestId;

    public string MergeRequestId { get; }
}
