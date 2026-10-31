using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Contracts;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Audit;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Customers;

/// <summary>
/// Customer merges: a frozen plan after approval, the source closed with retained history, a permanent alias,
/// a published merge event and completion that waits for every required consumer acknowledgement.
/// Decision: P1-E03-D11.
/// </summary>
public sealed class CustomerMergeService : ICustomerMergeService
{
    private static readonly HashSet<CustomerStatus> MergeableStates =
        [CustomerStatus.Active, CustomerStatus.Restricted, CustomerStatus.Dormant];

    private readonly CustomerOnboardingDbContext _context;
    private readonly TimeProvider _timeProvider;

    public CustomerMergeService(CustomerOnboardingDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CustomerMergeView> ProposeAsync(
        ProposeCustomerMergeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        CustomerRecord survivor = await LoadCustomerAsync(command.SurvivorCustomerId, cancellationToken).ConfigureAwait(false);
        CustomerRecord source = await LoadCustomerAsync(command.SourceCustomerId, cancellationToken).ConfigureAwait(false);

        EnsureMergeable(survivor);
        EnsureMergeable(source);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        CustomerMergeRequest request;
        try
        {
            request = CustomerMergeRequest.Propose(
                command.InstitutionId,
                command.SurvivorCustomerId,
                command.SourceCustomerId,
                command.Reason,
                command.RequiredConsumers,
                now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        MergeRequestRecord record = ToRecord(request);
        _context.MergeRequests.Add(record);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            DiscardPendingChanges();
            throw Conflict("merge_source_conflict", "This source customer already has an active merge request.");
        }

        return ToView(request);
    }

    public async Task<CustomerMergeView> SubmitForApprovalAsync(
        SubmitCustomerMergeForApprovalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        MergeRequestRecord record = await LoadRequestAsync(command.MergeRequestId, cancellationToken).ConfigureAwait(false);
        CustomerMergeRequest request = ToModel(record);

        if (string.Equals(MergeCodes.ToCode(request.Status), "pending_approval", StringComparison.Ordinal))
        {
            // One approval request per merge plan: the plan hash is frozen, so a repeat has nothing new to send.
            return ToView(request);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        try
        {
            request.Analyse(now);
            request.SubmitForApproval(now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Apply(request, record);

        CustomerEventWriter.Add(
            _context,
            command.InstitutionId,
            request.MergeRequestId,
            CustomerClassificationApprovalV1.RequestedEventType,
            new CustomerApprovalRequestedV1(
                command.InstitutionId.Value,
                request.MergeRequestId,
                request.MergeRequestId,
                CustomerMergeApprovalV1.WorkflowKey,
                CustomerMergeApprovalV1.WorkflowVersionId,
                CustomerMergeApprovalV1.DefinitionHash,
                CustomerMergeApprovalV1.SourceService,
                CustomerMergeApprovalV1.SubjectType,
                request.MergeRequestId,
                "1",
                request.PlanHash,
                command.ActorId.Value,
                CustomerMergeApprovalV1.RequiredPermission,
                command.CorrelationId,
                now),
            command.CorrelationId,
            now,
            CustomerClassificationApprovalV1.EventFamily);

        AuditRecorder.Record(
            _context,
            command.InstitutionId,
            command.ActorId.Value,
            AuditRecorder.ActionCustomerMergeSubmitted,
            "merge_request",
            request.MergeRequestId,
            command.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["planHash"] = request.PlanHash,
                ["survivorCustomerId"] = request.SurvivorCustomerId,
                ["sourceCustomerId"] = request.SourceCustomerId,
            },
            now);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToView(request);
    }

    public async Task<CustomerMergeView> ApproveAsync(
        ApproveCustomerMergeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        MergeRequestRecord record = await LoadRequestAsync(command.MergeRequestId, cancellationToken).ConfigureAwait(false);
        CustomerMergeRequest request = ToModel(record);

        try
        {
            request.Analyse(_timeProvider.GetUtcNow());
            request.SubmitForApproval(_timeProvider.GetUtcNow());
            request.Approve(command.WorkflowDecisionReference, _timeProvider.GetUtcNow());
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Apply(request, record);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToView(request);
    }

    public async Task<CustomerMergeView> ApplyAsync(
        ApplyCustomerMergeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        MergeRequestRecord record = await LoadRequestAsync(command.MergeRequestId, cancellationToken).ConfigureAwait(false);
        CustomerMergeRequest request = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        CustomerRecord sourceRecord = await _context.Customers
            .Include(customer => customer.Classifications)
            .Include(customer => customer.StatusHistory)
            .SingleOrDefaultAsync(customer => customer.CustomerId == request.SourceCustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CustomerNotFoundException(request.SourceCustomerId);

        try
        {
            request.BeginApplying(now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Customer customer = CustomerService.ToModel(sourceRecord);
        try
        {
            customer.ChangeStatus(
                CustomerStatus.Closed,
                $"merged into {request.SurvivorCustomerId}: {request.Reason}",
                command.ActorId.Value,
                request.ApprovalReference,
                now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        CustomerService.Apply(customer, sourceRecord);

        _context.CustomerAliases.Add(new CustomerAliasRecord
        {
            InstitutionId = record.InstitutionId,
            AliasCustomerId = request.SourceCustomerId,
            SurvivorCustomerId = request.SurvivorCustomerId,
            MergeRequestId = request.MergeRequestId,
            Reason = request.Reason,
            CreatedBy = command.ActorId.Value,
            CreatedAt = now,
        });

        CustomerEventWriter.Add(
            _context,
            command.InstitutionId,
            request.SurvivorCustomerId,
            CustomerContractV1.CustomersMergedEventType,
            new CustomersMergedV1(
                request.MergeRequestId,
                request.SurvivorCustomerId,
                request.SourceCustomerId,
                command.InstitutionId.Value,
                request.PlanHash,
                request.ApprovalReference,
                now),
            command.CorrelationId,
            now);

        try
        {
            request.RequireReconciliation(now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Apply(request, record);

        AuditRecorder.Record(
            _context,
            command.InstitutionId,
            command.ActorId.Value,
            AuditRecorder.ActionCustomerMergeApplied,
            AuditRecorder.SubjectCustomer,
            request.SurvivorCustomerId,
            command.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mergeRequestId"] = request.MergeRequestId,
                ["survivorCustomerId"] = request.SurvivorCustomerId,
                ["sourceCustomerId"] = request.SourceCustomerId,
                ["planHash"] = request.PlanHash,
            },
            now);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            DiscardPendingChanges();
            throw Conflict("merge_alias_conflict", "The source customer has already been merged.");
        }

        return ToView(request);
    }

    public async Task<CustomerMergeView> AcknowledgeAsync(
        AcknowledgeCustomerMergeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        MergeRequestRecord record = await LoadRequestAsync(command.MergeRequestId, cancellationToken).ConfigureAwait(false);
        CustomerMergeRequest request = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        try
        {
            request.Acknowledge(command.Consumer, now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Apply(request, record);

        if (!record.Acknowledgements.Any(acknowledgement =>
            string.Equals(acknowledgement.Consumer, command.Consumer.Trim().ToLowerInvariant(), StringComparison.Ordinal)))
        {
            record.Acknowledgements.Add(new MergeConsumerStatusRecord
            {
                InstitutionId = record.InstitutionId,
                MergeRequestId = record.MergeRequestId,
                Consumer = command.Consumer.Trim().ToLowerInvariant(),
                AcknowledgedAt = now,
            });
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToView(request);
    }

    public async Task<CustomerMergeView> WithdrawAsync(
        WithdrawCustomerMergeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        MergeRequestRecord record = await LoadRequestAsync(command.MergeRequestId, cancellationToken).ConfigureAwait(false);
        CustomerMergeRequest request = ToModel(record);

        try
        {
            request.Withdraw(command.Reason, _timeProvider.GetUtcNow());
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Apply(request, record);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToView(request);
    }

    public async Task<CustomerMergeView> GetAsync(GetCustomerMergeQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        MergeRequestRecord record = await _context.MergeRequests
            .AsNoTracking()
            .Include(request => request.Acknowledgements)
            .SingleOrDefaultAsync(request => request.MergeRequestId == query.MergeRequestId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CustomerMergeNotFoundException(query.MergeRequestId);

        return ToView(ToModel(record));
    }

    private static void EnsureMergeable(CustomerRecord record)
    {
        CustomerStatus status = CustomerCodes.FromStatusCode(record.Status);
        if (!MergeableStates.Contains(status))
        {
            throw Conflict(
                "merge_customer_state_invalid",
                $"A customer in state '{record.Status}' cannot take part in a merge.");
        }
    }

    private async Task<CustomerRecord> LoadCustomerAsync(string customerId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        CustomerRecord? record = await _context.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(customer => customer.CustomerId == customerId, cancellationToken)
            .ConfigureAwait(false);

        return record ?? throw new CustomerNotFoundException(customerId);
    }

    private async Task<MergeRequestRecord> LoadRequestAsync(string mergeRequestId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mergeRequestId);

        MergeRequestRecord? record = await _context.MergeRequests
            .Include(request => request.Acknowledgements)
            .SingleOrDefaultAsync(request => request.MergeRequestId == mergeRequestId, cancellationToken)
            .ConfigureAwait(false);

        return record ?? throw new CustomerMergeNotFoundException(mergeRequestId);
    }

    private static MergeRequestRecord ToRecord(CustomerMergeRequest request) => new()
    {
        InstitutionId = request.InstitutionId.Value,
        MergeRequestId = request.MergeRequestId,
        SurvivorCustomerId = request.SurvivorCustomerId,
        SourceCustomerId = request.SourceCustomerId,
        Reason = request.Reason,
        PlanHash = request.PlanHash,
        Status = MergeCodes.ToCode(request.Status),
        RequiredConsumers = JsonSerializer.Serialize(request.RequiredConsumers),
        ApprovalReference = request.ApprovalReference,
        CreatedAt = request.CreatedAt,
        UpdatedAt = request.UpdatedAt,
        CompletedAt = request.CompletedAt,
        Acknowledgements =
        [
            .. request.Acknowledgements.Select(acknowledgement => new MergeConsumerStatusRecord
            {
                InstitutionId = request.InstitutionId.Value,
                MergeRequestId = request.MergeRequestId,
                Consumer = acknowledgement.Consumer,
                AcknowledgedAt = acknowledgement.AcknowledgedAt,
            }),
        ],
    };

    internal static CustomerMergeRequest ToMergeModel(MergeRequestRecord record) => ToModel(record);

    private static CustomerMergeRequest ToModel(MergeRequestRecord record) =>
        CustomerMergeRequest.Restore(
            InstitutionId.From(record.InstitutionId),
            record.MergeRequestId,
            record.SurvivorCustomerId,
            record.SourceCustomerId,
            record.Reason,
            record.PlanHash,
            DeserialiseConsumers(record.RequiredConsumers),
            [
                .. record.Acknowledgements
                    .OrderBy(acknowledgement => acknowledgement.AcknowledgedAt)
                    .Select(acknowledgement => new MergeConsumerAcknowledgement(
                        acknowledgement.Consumer,
                        acknowledgement.AcknowledgedAt)),
            ],
            MergeCodes.FromStatusCode(record.Status),
            record.ApprovalReference,
            record.CreatedAt,
            record.UpdatedAt,
            record.CompletedAt);

    private static List<string> DeserialiseConsumers(string json)
    {
        List<string>? consumers = JsonSerializer.Deserialize<List<string>>(json);
        if (consumers is null || consumers.Count == 0)
        {
            throw new OnboardingRuleException("merge_consumers_invalid", "A stored merge plan has no required consumers.");
        }

        return consumers;
    }

    internal static void ApplyMerge(CustomerMergeRequest request, MergeRequestRecord record) => Apply(request, record);

    private static void Apply(CustomerMergeRequest request, MergeRequestRecord record)
    {
        record.Status = MergeCodes.ToCode(request.Status);
        record.ApprovalReference = request.ApprovalReference;
        record.UpdatedAt = request.UpdatedAt;
        record.CompletedAt = request.CompletedAt;
    }

    private static CustomerMergeView ToView(CustomerMergeRequest request) =>
        new(
            request.MergeRequestId,
            request.SurvivorCustomerId,
            request.SourceCustomerId,
            request.Reason,
            request.PlanHash,
            request.Status,
            MergeCodes.ToCode(request.Status),
            request.ApprovalReference,
            request.CreatedAt,
            request.UpdatedAt,
            request.CompletedAt,
            request.RequiredConsumers,
            request.OutstandingConsumers,
            [
                .. request.Acknowledgements.Select(acknowledgement => new MergeAcknowledgementView(
                    acknowledgement.Consumer,
                    acknowledgement.AcknowledgedAt)),
            ]);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres
        && postgres.SqlState == PostgresErrorCodes.UniqueViolation;

    private static OnboardingApplicationConflictException Conflict(string code, string message) => new(code, message);

    private void DiscardPendingChanges()
    {
        foreach (EntityEntry entry in _context.ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    entry.State = EntityState.Unchanged;
                    break;
                default:
                    break;
            }
        }
    }
}
