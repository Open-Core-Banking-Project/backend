using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CustomerOnboarding.Application.Customers;
using CoreBanking.CustomerOnboarding.Application.Kyc;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Contracts;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Audit;
using CoreBanking.CustomerOnboarding.Infrastructure.Customers;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Customers;

/// <summary>
/// Protected-data corrections: an append-only record with the superseded and new protected-value references,
/// the affected verification invalidated so it is never treated as current, and a minimal profile-changed
/// event. Decision: epic 03 CUS-UC-06, invariant 15.
/// </summary>
public sealed class CustomerCorrectionService : ICustomerCorrectionService
{
    private readonly CustomerOnboardingDbContext _context;
    private readonly IKycCaseService _kyc;
    private readonly TimeProvider _timeProvider;

    public CustomerCorrectionService(
        CustomerOnboardingDbContext context,
        IKycCaseService kyc,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _kyc = kyc ?? throw new ArgumentNullException(nameof(kyc));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CustomerCorrectionView> RecordAsync(
        RecordCustomerCorrectionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        bool exists = await _context.Customers
            .AsNoTracking()
            .AnyAsync(customer => customer.CustomerId == command.CustomerId, cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
        {
            throw new CustomerNotFoundException(command.CustomerId);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        CustomerCorrection correction;
        try
        {
            correction = CustomerCorrection.Record(
                command.InstitutionId,
                command.CustomerId,
                command.FieldCode,
                command.PriorValueReference,
                command.NewValueReference,
                command.Reason,
                command.EvidenceReference,
                command.IsVerifiedIdentifier,
                command.RequirementCode,
                command.ApprovalReference,
                command.ActorId.Value,
                now);
        }
        catch (OnboardingRuleException exception)
        {
            throw Conflict(exception.Code, exception.Message);
        }

        _context.CustomerCorrections.Add(new CustomerCorrectionRecord
        {
            InstitutionId = command.InstitutionId.Value,
            CorrectionId = correction.CorrectionId,
            CustomerId = correction.CustomerId,
            FieldCode = correction.FieldCode,
            PriorValueReference = correction.PriorValueReference,
            NewValueReference = correction.NewValueReference,
            Reason = correction.Reason,
            EvidenceReference = correction.EvidenceReference,
            RequiresReverification = correction.RequiresReverification,
            RequirementCode = correction.RequirementCode,
            ApprovalReference = correction.ApprovalReference,
            RecordedBy = correction.RecordedBy,
            RecordedAt = correction.RecordedAt,
        });

        CustomerEventWriter.Add(
            _context,
            command.InstitutionId,
            correction.CorrectionId,
            CustomerContractV1.CustomerProfileChangedEventType,
            new CustomerProfileChangedV1(
                correction.CorrectionId,
                correction.CustomerId,
                command.InstitutionId.Value,
                correction.FieldCode,
                correction.RequiresReverification,
                correction.RequirementCode,
                correction.ApprovalReference,
                now),
            command.CorrelationId,
            now);

        AuditRecorder.Record(
            _context,
            command.InstitutionId,
            command.ActorId.Value,
            AuditRecorder.ActionCustomerCorrectionRecorded,
            AuditRecorder.SubjectCustomer,
            correction.CustomerId,
            command.CorrelationId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["correctionId"] = correction.CorrectionId,
                ["fieldCode"] = correction.FieldCode,
                ["requiresReverification"] = correction.RequiresReverification ? "true" : "false",
                ["requirementCode"] = correction.RequirementCode ?? string.Empty,
                ["approvalReference"] = correction.ApprovalReference ?? string.Empty,
            },
            now);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (correction.RequirementCode is not null)
        {
            await ReopenVerificationAsync(command, correction, cancellationToken).ConfigureAwait(false);
        }

        return ToView(correction);
    }

    public async Task<IReadOnlyList<CustomerCorrectionView>> ListAsync(
        GetCustomerCorrectionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return
        [
            .. (await _context.CustomerCorrections
                .AsNoTracking()
                .Where(record => record.CustomerId == query.CustomerId)
                .OrderByDescending(record => record.RecordedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
                .Select(record => new CustomerCorrectionView(
                    record.CorrectionId,
                    record.CustomerId,
                    record.FieldCode,
                    record.RequiresReverification,
                    record.RequirementCode,
                    record.ApprovalReference,
                    record.RecordedBy,
                    record.RecordedAt)),
        ];
    }

    private async Task ReopenVerificationAsync(
        RecordCustomerCorrectionCommand command,
        CustomerCorrection correction,
        CancellationToken cancellationToken)
    {
        // The case belongs to the party behind the customer, so a correction cannot reach another party's case.
        string? partyId = await _context.Customers
            .AsNoTracking()
            .Where(customer => customer.CustomerId == correction.CustomerId)
            .Select(customer => customer.PartyId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        KycCaseRecord? kycCase = await _context.KycCases
            .Include(record => record.Requirements)
            .Where(record => record.PartyId == partyId)
            .OrderByDescending(record => record.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (kycCase is null)
        {
            throw Conflict(
                "customer_correction_kyc_case_missing",
                "A correction that invalidates evidence requires the customer's KYC case.");
        }

        KycRequirementRecord? requirement = kycCase.Requirements
            .SingleOrDefault(record => string.Equals(record.RequirementCode, correction.RequirementCode, StringComparison.Ordinal));

        if (requirement is null)
        {
            throw Conflict(
                "customer_correction_requirement_missing",
                "The requirement named by the correction is not part of the customer's KYC case.");
        }

        await _kyc.ReopenRequirementAsync(
            new ReopenKycRequirementCommand(
                command.InstitutionId,
                command.ActorId,
                kycCase.CaseId,
                correction.RequirementCode!,
                "protected data corrected: " + correction.CorrectionId,
                command.CorrelationId),
            cancellationToken).ConfigureAwait(false);
    }

    private static CustomerCorrectionView ToView(CustomerCorrection correction) =>
        new(
            correction.CorrectionId,
            correction.CustomerId,
            correction.FieldCode,
            correction.RequiresReverification,
            correction.RequirementCode,
            correction.ApprovalReference,
            correction.RecordedBy,
            correction.RecordedAt);

    private static OnboardingApplicationConflictException Conflict(string code, string message) => new(code, message);
}
