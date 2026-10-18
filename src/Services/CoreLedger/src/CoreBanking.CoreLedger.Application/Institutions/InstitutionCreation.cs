using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.CoreLedger.Application.Institutions;

/// <summary>A platform-authorised request to create and begin provisioning one institution.</summary>
public sealed record CreateInstitutionCommand(
    InstitutionId InstitutionId,
    ActorId ActorId,
    string IdempotencyKey,
    string Code,
    string LegalName,
    string CountryCode,
    string BaseCurrency,
    string CorrelationId);

/// <summary>The stable outcome returned for both an execution and an idempotent replay.</summary>
public sealed record CreateInstitutionResult(
    string InstitutionId,
    string ProvisioningRunId,
    string EventId,
    bool Replayed);

public interface IInstitutionCreationService
{
    Task<CreateInstitutionResult> CreateAsync(
        CreateInstitutionCommand command,
        CancellationToken cancellationToken);
}

public sealed class InstitutionCreationConflictException : Exception
{
    public InstitutionCreationConflictException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public InstitutionCreationConflictException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class InstitutionCreationInProgressException : Exception
{
    public InstitutionCreationInProgressException(string message)
        : base(message)
    {
    }

    public InstitutionCreationInProgressException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
