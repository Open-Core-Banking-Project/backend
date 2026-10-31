using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Contracts;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Audit;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Customers;

/// <summary>
/// The institution banking relationship: number allocation from the institution sequence, versioned
/// classification with Workflow approval, activation gated by a completed KYC case, and the epic's status
/// lifecycle published as events. It never changes an account. Decision: P1-E03-D09, P1-E03-D10.
/// </summary>
public sealed class CustomerService : ICustomerService
{
    private readonly CustomerOnboardingDbContext _context;
    private readonly TimeProvider _timeProvider;

    public CustomerService(CustomerOnboardingDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CustomerView> RegisterAsync(RegisterCustomerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        OnboardingApplicationRecord application = await _context.OnboardingApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.ApplicationId == command.ApplicationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OnboardingApplicationNotFoundException(command.ApplicationId);

        if (string.IsNullOrWhiteSpace(application.PartyId) || application.Status != "submitted")
        {
            throw Conflict("customer_application_not_submitted", "A customer relationship is created only for a submitted application.");
        }

        bool exists = await _context.Customers
            .AnyAsync(record => record.PartyId == application.PartyId, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            throw Conflict("customer_exists", "This party already has a customer relationship in the institution.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string customerNumber = await AllocateNumberAsync(command.InstitutionId, cancellationToken).ConfigureAwait(false);

        Customer customer = Customer.Register(
            command.InstitutionId,
            application.PartyId,
            customerNumber,
            command.BranchId ?? application.BranchId,
            now);

        CustomerRecord record = ToRecord(customer);
        _context.Customers.Add(record);
        CustomerEventWriter.Add(
            _context,
            command.InstitutionId,
            customer.CustomerId,
            CustomerContractV1.CustomerCreatedEventType,
            new CustomerCreatedV1(
                customer.CustomerId,
                customer.PartyId,
                command.InstitutionId.Value,
                customer.BranchId,
                customer.CustomerNumber,
                CustomerCodes.ToCode(customer.Status),
                now),
            command.CorrelationId,
            now);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            DiscardPendingChanges();
            throw Conflict("customer_exists", "This party already has a customer relationship in the institution.");
        }

        return await BuildViewAsync(customer).ConfigureAwait(false);
    }

    public async Task<CustomerView> ProposeClassificationAsync(
        ProposeClassificationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        CustomerRecord record = await LoadRecordAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);
        Customer customer = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        try
        {
            customer.ProposeClassification(
                command.Tier,
                command.Segment,
                command.RiskClass,
                command.RequiresEnhancedDueDiligence,
                now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Apply(customer, record);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await BuildViewAsync(customer).ConfigureAwait(false);
    }

    public async Task<CustomerView> ApproveClassificationAsync(
        ApproveClassificationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        CustomerRecord record = await LoadRecordAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);
        Customer customer = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        try
        {
            customer.ApproveClassification(command.Version, command.WorkflowDecisionReference, now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Apply(customer, record);

        CustomerClassification approved = customer.Classifications.Single(classification => classification.Version == command.Version);
        AuditRecorder.Record(
            _context,
            command.InstitutionId,
            command.ActorId.Value,
            AuditRecorder.ActionCustomerClassificationApproved,
            AuditRecorder.SubjectCustomer,
            customer.CustomerId,
            command.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["version"] = approved.Version.ToString(CultureInfo.InvariantCulture),
                ["tier"] = approved.Tier,
                ["segment"] = approved.Segment,
                ["riskClass"] = approved.RiskClass,
                ["enhancedDueDiligence"] = approved.RequiresEnhancedDueDiligence ? "true" : "false",
            },
            now);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            DiscardPendingChanges();
            throw Conflict("customer_classification_conflict", "Another classification version is already approved for this customer.");
        }

        return await BuildViewAsync(customer).ConfigureAwait(false);
    }

    public async Task<ClassificationApprovalView> SubmitClassificationForApprovalAsync(
        SubmitClassificationForApprovalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        CustomerRecord record = await LoadRecordAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);
        Customer customer = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        CustomerClassification classification = customer.Classifications
            .SingleOrDefault(candidate => candidate.Version == command.Version)
            ?? throw Conflict("customer_classification_not_found", "The classification version does not exist for this customer.");

        string contentHash = CustomerClassification.ComputeContentHash(
            customer.CustomerId,
            classification.Version,
            classification.Tier,
            classification.Segment,
            classification.RiskClass,
            classification.RequiresEnhancedDueDiligence);

        if (classification.ApprovalRequestId is not null
            && string.Equals(classification.ApprovalContentHash, contentHash, StringComparison.Ordinal))
        {
            return new ClassificationApprovalView(
                customer.CustomerId,
                classification.Version,
                classification.ApprovalRequestId,
                contentHash,
                classification.SubmittedForApprovalAt ?? now,
                IsReplay: true);
        }

        string requestId = Guid.NewGuid().ToString("N");
        try
        {
            classification.SubmitForApproval(requestId, contentHash, now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Apply(customer, record);

        CustomerEventWriter.Add(
            _context,
            command.InstitutionId,
            requestId,
            CustomerClassificationApprovalV1.RequestedEventType,
            new CustomerApprovalRequestedV1(
                command.InstitutionId.Value,
                requestId,
                customer.CustomerId + ":" + classification.Version.ToString(CultureInfo.InvariantCulture),
                CustomerClassificationApprovalV1.WorkflowKey,
                CustomerClassificationApprovalV1.WorkflowVersionId,
                CustomerClassificationApprovalV1.DefinitionHash,
                CustomerClassificationApprovalV1.SourceService,
                CustomerClassificationApprovalV1.SubjectType,
                customer.CustomerId,
                classification.Version.ToString(CultureInfo.InvariantCulture),
                contentHash,
                command.ActorId.Value,
                CustomerClassificationApprovalV1.RequiredPermission,
                command.CorrelationId,
                now),
            command.CorrelationId,
            now,
            CustomerClassificationApprovalV1.EventFamily);

        AuditRecorder.Record(
            _context,
            command.InstitutionId,
            command.ActorId.Value,
            AuditRecorder.ActionCustomerClassificationSubmitted,
            AuditRecorder.SubjectCustomer,
            customer.CustomerId,
            command.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["version"] = classification.Version.ToString(CultureInfo.InvariantCulture),
                ["approvalRequestId"] = requestId,
                ["contentHash"] = contentHash,
            },
            now);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ClassificationApprovalView(
            customer.CustomerId,
            classification.Version,
            requestId,
            contentHash,
            now,
            IsReplay: false);
    }

    public async Task<CustomerView> ActivateAsync(ActivateCustomerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        CustomerRecord record = await LoadRecordAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);
        Customer customer = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await EnsureKycCompleteAsync(customer, cancellationToken).ConfigureAwait(false);

        CustomerStatus previous = customer.Status;
        try
        {
            customer.Activate(command.ClassificationVersion, now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Apply(customer, record);
        AddStatusChangedEvent(command.InstitutionId, customer, previous, now, command.CorrelationId);
        AuditRecorder.Record(
            _context,
            command.InstitutionId,
            command.ActorId.Value,
            AuditRecorder.ActionCustomerStatusChanged,
            AuditRecorder.SubjectCustomer,
            customer.CustomerId,
            command.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["from"] = CustomerCodes.ToCode(previous),
                ["to"] = CustomerCodes.ToCode(customer.Status),
                ["approvalReference"] = customer.StatusHistory[^1].ApprovalReference ?? string.Empty,
                ["classificationVersion"] = command.ClassificationVersion.ToString(CultureInfo.InvariantCulture),
            },
            now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await BuildViewAsync(customer).ConfigureAwait(false);
    }

    public async Task<CustomerView> ChangeStatusAsync(
        ChangeCustomerStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        CustomerRecord record = await LoadRecordAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);
        Customer customer = ToModel(record);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        CustomerStatus previous = customer.Status;

        try
        {
            customer.ChangeStatus(
                command.TargetStatus,
                command.Reason,
                command.ActorId.Value,
                command.ApprovalReference,
                now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        Apply(customer, record);
        AddStatusChangedEvent(command.InstitutionId, customer, previous, now, command.CorrelationId);
        AuditRecorder.Record(
            _context,
            command.InstitutionId,
            command.ActorId.Value,
            AuditRecorder.ActionCustomerStatusChanged,
            AuditRecorder.SubjectCustomer,
            customer.CustomerId,
            command.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["from"] = CustomerCodes.ToCode(previous),
                ["to"] = CustomerCodes.ToCode(customer.Status),
                ["approvalReference"] = customer.StatusHistory[^1].ApprovalReference ?? string.Empty,
            },
            now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await BuildViewAsync(customer).ConfigureAwait(false);
    }

    public async Task<CustomerView> GetAsync(GetCustomerQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        CustomerRecord record = await _context.Customers
            .AsNoTracking()
            .Include(customer => customer.Classifications)
            .Include(customer => customer.StatusHistory)
            .SingleOrDefaultAsync(customer => customer.CustomerId == query.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CustomerNotFoundException(query.CustomerId);

        return ToView(ToModel(record));
    }

    /// <summary>
    /// Activation needs a completed KYC case for the submitted application. Absence is a refusal, never an
    /// assumption. Decision: P1-E03-D09.
    /// </summary>
    private async Task EnsureKycCompleteAsync(Customer customer, CancellationToken cancellationToken)
    {
        string? applicationId = await _context.OnboardingApplications
            .AsNoTracking()
            .Where(application => application.PartyId == customer.PartyId && application.Status == "submitted")
            .Select(application => application.ApplicationId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (applicationId is null)
        {
            throw Conflict("customer_application_not_submitted", "No submitted application exists for this customer's party.");
        }

        string? caseStatus = await _context.KycCases
            .AsNoTracking()
            .Where(kycCase => kycCase.ApplicationId == applicationId)
            .Select(kycCase => kycCase.Status)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(caseStatus, "completed", StringComparison.Ordinal))
        {
            throw Conflict("kyc_case_incomplete", "Activation requires a completed KYC case for the application.");
        }
    }

    private async Task<CustomerRecord> LoadRecordAsync(string customerId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        CustomerRecord? record = await _context.Customers
            .Include(customer => customer.Classifications)
            .Include(customer => customer.StatusHistory)
            .SingleOrDefaultAsync(customer => customer.CustomerId == customerId, cancellationToken)
            .ConfigureAwait(false);

        return record ?? throw new CustomerNotFoundException(customerId);
    }

    /// <summary>
    /// Allocates the next institution number atomically. A failed create may leave a gap; the contract is
    /// uniqueness and non-reuse, not contiguity. Decision: P1-E03-D02.
    /// </summary>
    private async Task<string> AllocateNumberAsync(InstitutionId institutionId, CancellationToken cancellationToken)
    {
        await _context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DbConnection connection = _context.Database.GetDbConnection();
            await using DbCommand command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO customer.customer_number_sequences AS sequences (institution_id, next_value)
                VALUES (@institution, 1)
                ON CONFLICT (institution_id) DO UPDATE SET next_value = sequences.next_value + 1
                RETURNING next_value;
                """;

            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "institution";
            parameter.Value = institutionId.Value;
            command.Parameters.Add(parameter);

            object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToString(result, CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException("The customer number sequence returned no value.");
        }
        finally
        {
            await _context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private void AddStatusChangedEvent(
        InstitutionId institutionId,
        Customer customer,
        CustomerStatus previous,
        DateTimeOffset now,
        string correlationId)
    {
        CustomerStatusChange change = customer.StatusHistory[^1];
        CustomerEventWriter.Add(
            _context,
            institutionId,
            customer.CustomerId,
            CustomerContractV1.CustomerStatusChangedEventType,
            new CustomerStatusChangedV1(
                customer.CustomerId,
                institutionId.Value,
                CustomerCodes.ToCode(previous),
                CustomerCodes.ToCode(customer.Status),
                change.ApprovalReference,
                now),
            correlationId,
            now);
    }


    private static Task<CustomerView> BuildViewAsync(Customer customer) => Task.FromResult(ToView(customer));

    private static CustomerRecord ToRecord(Customer customer) => new()
    {
        InstitutionId = customer.InstitutionId.Value,
        CustomerId = customer.CustomerId,
        PartyId = customer.PartyId,
        CustomerNumber = customer.CustomerNumber,
        BranchId = customer.BranchId,
        Status = CustomerCodes.ToCode(customer.Status),
        CreatedAt = customer.CreatedAt,
        UpdatedAt = customer.UpdatedAt,
        ConcurrencyVersion = customer.ConcurrencyVersion,
        Classifications =
        [
            .. customer.Classifications.Select(classification => new CustomerClassificationRecord
            {
                InstitutionId = customer.InstitutionId.Value,
                CustomerId = customer.CustomerId,
                Version = classification.Version,
                Tier = classification.Tier,
                Segment = classification.Segment,
                RiskClass = classification.RiskClass,
                RequiresEnhancedDueDiligence = classification.RequiresEnhancedDueDiligence,
                Status = CustomerCodes.ToCode(classification.Status),
                WorkflowDecisionReference = classification.WorkflowDecisionReference,
                ApprovedAt = classification.ApprovedAt,
                ProposedAt = classification.ProposedAt,
                UpdatedAt = classification.UpdatedAt,
            }),
        ],
        StatusHistory =
        [
            .. customer.StatusHistory.Select((change, index) => ToRecord(customer.InstitutionId.Value, customer.CustomerId, index + 1, change)),
        ],
    };

    private static CustomerStatusHistoryRecord ToRecord(
        string institutionId,
        string customerId,
        int sequence,
        CustomerStatusChange change) =>
        new()
        {
            InstitutionId = institutionId,
            CustomerId = customerId,
            Sequence = sequence,
            FromStatus = CustomerCodes.ToCode(change.From),
            ToStatus = CustomerCodes.ToCode(change.To),
            Reason = change.Reason,
            ActorId = change.ActorId,
            ApprovalReference = change.ApprovalReference,
            OccurredAt = change.OccurredAt,
        };

    internal static Customer ToModel(CustomerRecord record) =>
        Customer.Restore(
            InstitutionId.From(record.InstitutionId),
            record.CustomerId,
            record.PartyId,
            record.CustomerNumber,
            record.BranchId,
            CustomerCodes.FromStatusCode(record.Status),
            [
                .. record.Classifications
                    .OrderBy(classification => classification.Version)
                    .Select(classification => CustomerClassification.Restore(
                        classification.Version,
                        classification.Tier,
                        classification.Segment,
                        classification.RiskClass,
                        classification.RequiresEnhancedDueDiligence,
                        CustomerCodes.FromClassificationStatusCode(classification.Status),
                        classification.WorkflowDecisionReference,
                        classification.ApprovedAt,
                        classification.ProposedAt,
                        classification.UpdatedAt,
                        classification.ApprovalRequestId,
                        classification.ApprovalContentHash,
                        classification.SubmittedForApprovalAt)),
            ],
            [
                .. record.StatusHistory
                    .OrderBy(history => history.Sequence)
                    .Select(history => new CustomerStatusChange(
                        CustomerCodes.FromStatusCode(history.FromStatus),
                        CustomerCodes.FromStatusCode(history.ToStatus),
                        history.Reason,
                        history.ActorId,
                        history.ApprovalReference,
                        history.OccurredAt)),
            ],
            record.CreatedAt,
            record.UpdatedAt,
            record.ConcurrencyVersion);

    internal static void Apply(Customer customer, CustomerRecord record)
    {
        record.Status = CustomerCodes.ToCode(customer.Status);
        record.UpdatedAt = customer.UpdatedAt;
        record.ConcurrencyVersion = customer.ConcurrencyVersion;

        foreach (CustomerClassification classification in customer.Classifications)
        {
            CustomerClassificationRecord? row = record.Classifications.SingleOrDefault(candidate =>
                candidate.Version == classification.Version);

            if (row is null)
            {
                record.Classifications.Add(new CustomerClassificationRecord
                {
                    InstitutionId = record.InstitutionId,
                    CustomerId = record.CustomerId,
                    Version = classification.Version,
                    Tier = classification.Tier,
                    Segment = classification.Segment,
                    RiskClass = classification.RiskClass,
                    RequiresEnhancedDueDiligence = classification.RequiresEnhancedDueDiligence,
                    Status = CustomerCodes.ToCode(classification.Status),
                    WorkflowDecisionReference = classification.WorkflowDecisionReference,
                    ApprovalRequestId = classification.ApprovalRequestId,
                    ApprovalContentHash = classification.ApprovalContentHash,
                    SubmittedForApprovalAt = classification.SubmittedForApprovalAt,
                    ApprovedAt = classification.ApprovedAt,
                    ProposedAt = classification.ProposedAt,
                    UpdatedAt = classification.UpdatedAt,
                });
                continue;
            }

            row.Status = CustomerCodes.ToCode(classification.Status);
            row.WorkflowDecisionReference = classification.WorkflowDecisionReference;
            row.ApprovalRequestId = classification.ApprovalRequestId;
            row.ApprovalContentHash = classification.ApprovalContentHash;
            row.SubmittedForApprovalAt = classification.SubmittedForApprovalAt;
            row.ApprovedAt = classification.ApprovedAt;
            row.UpdatedAt = classification.UpdatedAt;
        }

        int recorded = record.StatusHistory.Count;
        for (int index = recorded; index < customer.StatusHistory.Count; index++)
        {
            record.StatusHistory.Add(ToRecord(
                record.InstitutionId,
                record.CustomerId,
                index + 1,
                customer.StatusHistory[index]));
        }
    }

    private static CustomerView ToView(Customer customer) =>
        new(
            customer.CustomerId,
            customer.PartyId,
            customer.CustomerNumber,
            customer.BranchId,
            customer.Status,
            CustomerCodes.ToCode(customer.Status),
            customer.EffectiveClassification?.Version,
            customer.CreatedAt,
            customer.UpdatedAt,
            customer.ConcurrencyVersion,
            [
                .. customer.Classifications
                    .OrderBy(classification => classification.Version)
                    .Select(classification => new CustomerClassificationView(
                        classification.Version,
                        classification.Tier,
                        classification.Segment,
                        classification.RiskClass,
                        classification.RequiresEnhancedDueDiligence,
                        classification.Status,
                        CustomerCodes.ToCode(classification.Status),
                        classification.WorkflowDecisionReference,
                        classification.ApprovedAt,
                        classification.ProposedAt)),
            ],
            [
                .. customer.StatusHistory.Select((change, index) => new CustomerStatusChangeView(
                    index + 1,
                    change.From,
                    change.To,
                    CustomerCodes.ToCode(change.From),
                    CustomerCodes.ToCode(change.To),
                    change.Reason,
                    change.ActorId,
                    change.ApprovalReference,
                    change.OccurredAt)),
            ]);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException postgres
        && postgres.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation;

    private static OnboardingApplicationConflictException Conflict(string code, string message) => new(code, message);

    private void DiscardPendingChanges()
    {
        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry in _context.ChangeTracker.Entries().ToList())
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
