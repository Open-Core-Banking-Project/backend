using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Application.Privacy;
using CoreBanking.CustomerOnboarding.Contracts;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Audit;
using CoreBanking.CustomerOnboarding.Infrastructure.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Privacy;

/// <summary>
/// Effective-dated relationship-manager assignments: the current assignment is closed and the next is appended,
/// so history is retained and a customer always has at most one current manager.
/// Requirement: CUS-010, CUS-026.
/// </summary>
public sealed class RelationshipManagerService : IRelationshipManagerService
{
    private readonly CustomerOnboardingDbContext _context;
    private readonly TimeProvider _timeProvider;

    public RelationshipManagerService(CustomerOnboardingDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RelationshipManagerView> AssignAsync(
        AssignRelationshipManagerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        GuardCommand(command);

        await EnsureCustomerAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);

        List<RelationshipManagerAssignmentRecord> existing = await _context.RelationshipManagerAssignments
            .Where(record => record.CustomerId == command.CustomerId)
            .OrderByDescending(record => record.EffectiveFrom)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        RelationshipManagerAssignmentRecord? current = existing.FirstOrDefault(record => record.EffectiveTo is null);

        if (current is not null && string.Equals(current.ManagerActorId, command.ManagerActorId.Trim(), StringComparison.Ordinal))
        {
            return await GetAsync(
                new GetRelationshipManagerQuery(command.InstitutionId, command.ActorId, command.CustomerId),
                cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        RelationshipManagerAssignment assignment;
        try
        {
            assignment = RelationshipManagerAssignment.Assign(
                command.InstitutionId,
                command.CustomerId,
                command.ManagerActorId,
                command.ScopeBranchId,
                current?.AssignmentId,
                command.ActorId.Value,
                command.Reason,
                now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        if (current is not null)
        {
            current.EffectiveTo = now;
        }

        _context.RelationshipManagerAssignments.Add(new RelationshipManagerAssignmentRecord
        {
            InstitutionId = assignment.InstitutionId.Value,
            AssignmentId = assignment.AssignmentId,
            CustomerId = assignment.CustomerId,
            ManagerActorId = assignment.ManagerActorId,
            ScopeBranchId = assignment.ScopeBranchId,
            PreviousAssignmentId = assignment.PreviousAssignmentId,
            EffectiveFrom = assignment.EffectiveFrom,
            EffectiveTo = assignment.EffectiveTo,
            AssignedBy = assignment.AssignedBy,
            Reason = assignment.Reason,
        });

        CustomerEventWriter.Add(
            _context,
            command.InstitutionId,
            assignment.CustomerId,
            CustomerContractV1.RelationshipManagerAssignedEventType,
            new RelationshipManagerAssignedV1(
                assignment.AssignmentId,
                assignment.PreviousAssignmentId,
                assignment.CustomerId,
                assignment.InstitutionId.Value,
                assignment.ManagerActorId,
                assignment.ScopeBranchId,
                assignment.EffectiveFrom,
                now),
            command.CorrelationId,
            now);

        AuditRecorder.Record(
            _context,
            command.InstitutionId,
            command.ActorId.Value,
            AuditRecorder.ActionRelationshipManagerAssigned,
            AuditRecorder.SubjectCustomer,
            assignment.CustomerId,
            command.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["assignmentId"] = assignment.AssignmentId,
                ["managerActorId"] = assignment.ManagerActorId,
                ["previousAssignmentId"] = assignment.PreviousAssignmentId ?? string.Empty,
                ["scopeBranchId"] = assignment.ScopeBranchId ?? string.Empty,
            },
            now);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            DiscardPendingChanges();
            throw Conflict("customer_relationship_manager_conflict", "Another assignment was recorded at the same time.");
        }

        return await GetAsync(
            new GetRelationshipManagerQuery(command.InstitutionId, command.ActorId, command.CustomerId),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<RelationshipManagerView> GetAsync(
        GetRelationshipManagerQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.CustomerId))
        {
            throw Conflict("customer_relationship_manager_customer_required", "A customer identifier is required.");
        }

        await EnsureCustomerAsync(query.CustomerId, cancellationToken).ConfigureAwait(false);

        List<RelationshipManagerAssignmentRecord> records = await _context.RelationshipManagerAssignments
            .AsNoTracking()
            .Where(record => record.CustomerId == query.CustomerId)
            .OrderByDescending(record => record.EffectiveFrom)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        RelationshipManagerAssignmentView? current = records
            .Where(record => record.EffectiveTo is null)
            .Select(ToView)
            .FirstOrDefault();

        return new RelationshipManagerView(
            query.CustomerId,
            current,
            [.. records.Where(record => record.EffectiveTo is not null).Select(ToView)]);
    }

    private static RelationshipManagerAssignmentView ToView(RelationshipManagerAssignmentRecord record) =>
        new(
            record.AssignmentId,
            record.CustomerId,
            record.ManagerActorId,
            record.ScopeBranchId,
            record.PreviousAssignmentId,
            record.EffectiveFrom,
            record.EffectiveTo,
            record.AssignedBy,
            record.Reason);

    private static void GuardCommand(AssignRelationshipManagerCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.CustomerId))
        {
            throw Conflict("customer_relationship_manager_customer_required", "A customer identifier is required.");
        }

        if (string.IsNullOrWhiteSpace(command.ManagerActorId))
        {
            throw Conflict("customer_relationship_manager_invalid", "A relationship manager is required.");
        }
    }

    private async Task EnsureCustomerAsync(string customerId, CancellationToken cancellationToken)
    {
        bool exists = await _context.Customers
            .AsNoTracking()
            .AnyAsync(customer => customer.CustomerId == customerId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            throw new CustomerNotFoundException(customerId);
        }
    }

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
