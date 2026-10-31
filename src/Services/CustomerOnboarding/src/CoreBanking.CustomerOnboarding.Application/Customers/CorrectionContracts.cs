using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CustomerOnboarding.Application.Customers;

/// <summary>
/// Records a correction of one protected customer value. The caller supplies the approved field code, the
/// opaque protected-value references and, when the corrected attribute is verified, the approval reference.
/// Decision: epic 03 CUS-UC-06.
/// </summary>
public sealed record RecordCustomerCorrectionCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CustomerId,
    string FieldCode,
    string PriorValueReference,
    string NewValueReference,
    string Reason,
    string? EvidenceReference,
    bool IsVerifiedIdentifier,
    string? RequirementCode,
    string? ApprovalReference,
    string CorrelationId);

public sealed record CustomerCorrectionView(
    string CorrectionId,
    string CustomerId,
    string FieldCode,
    bool RequiresReverification,
    string? RequirementCode,
    string? ApprovalReference,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record GetCustomerCorrectionsQuery(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string CustomerId);

public interface ICustomerCorrectionService
{
    Task<CustomerCorrectionView> RecordAsync(RecordCustomerCorrectionCommand command, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerCorrectionView>> ListAsync(GetCustomerCorrectionsQuery query, CancellationToken cancellationToken);
}
