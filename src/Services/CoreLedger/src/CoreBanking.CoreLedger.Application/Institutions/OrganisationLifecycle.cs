using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Application.Institutions;

public sealed record CreateRegionCommand(InstitutionId InstitutionId, ActorId ActorId, string IdempotencyKey, string RegionId, string Code, string Name, string CorrelationId);
public sealed record CreateBranchCommand(InstitutionId InstitutionId, ActorId ActorId, string IdempotencyKey, string BranchId, string RegionId, string Code, string Name, string CorrelationId);
public sealed record ChangeOrganisationStatusCommand(InstitutionId InstitutionId, ActorId ActorId, string IdempotencyKey, string ResourceId, string TargetStatus, string? Reason, string CorrelationId);
public sealed record OrganisationLifecycleResult(string InstitutionId, string ResourceId, string EventId, string Status, DateTimeOffset ChangedAt, bool Replayed);

public interface IOrganisationLifecycleService
{
    Task<OrganisationLifecycleResult> CreateRegionAsync(CreateRegionCommand command, CancellationToken cancellationToken);
    Task<OrganisationLifecycleResult> ChangeRegionStatusAsync(ChangeOrganisationStatusCommand command, CancellationToken cancellationToken);
    Task<OrganisationLifecycleResult> CreateBranchAsync(CreateBranchCommand command, CancellationToken cancellationToken);
    Task<OrganisationLifecycleResult> ChangeBranchStatusAsync(ChangeOrganisationStatusCommand command, CancellationToken cancellationToken);
}

public sealed class OrganisationLifecycleRejectedException : Exception
{
    public OrganisationLifecycleRejectedException(string code, string message) : base(message) => Code = code;
    public OrganisationLifecycleRejectedException(string code, string message, Exception inner) : base(message, inner) => Code = code;
    public string Code { get; }
}

public sealed class OrganisationLifecycleInProgressException : Exception
{
    public OrganisationLifecycleInProgressException(string message) : base(message) { }
}
