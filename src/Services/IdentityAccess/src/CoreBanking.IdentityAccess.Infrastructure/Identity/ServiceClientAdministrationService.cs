using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Application.Authentication;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Domain.Identities;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IdentityAccess.Infrastructure.Identity;

/// <summary>
/// Registers, rotates and revokes service clients, and validates that a service token still maps to
/// an active unexpired client. Only an opaque credential reference is stored; the provider keeps
/// the secret and the rotation overlap.
/// Decision: ADR-0008, ADR-0011; slice 13.
/// </summary>
public sealed class ServiceClientAdministrationService : IServiceClientValidator
{
    private readonly IdentityAccessDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ServiceClientAdministrationService(IdentityAccessDbContext context, TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ServiceClientResultV1> RegisterAsync(
        InstitutionId institutionId,
        ActorId registeredBy,
        string clientName,
        string audience,
        string credentialReference,
        DateTimeOffset? credentialExpiresAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentNullException.ThrowIfNull(registeredBy);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string name = Normalise(() => ServiceClientRules.NormaliseName(clientName));
        string normalisedAudience = Normalise(() => ServiceClientRules.NormaliseAudience(audience));
        string reference = Normalise(() => ServiceClientRules.NormaliseReference(credentialReference));
        Normalise(() =>
        {
            ServiceClientRules.ValidateExpiry(credentialExpiresAt, now);
            return true;
        });

        ServiceClientRecord record = new()
        {
            InstitutionId = institutionId.Value,
            ServiceClientId = Guid.NewGuid().ToString("N"),
            ClientName = name,
            Audience = normalisedAudience,
            CredentialReference = reference,
            CredentialExpiresAt = credentialExpiresAt,
            Status = RecordStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _context.ServiceClients.Add(record);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToResult(record);
    }

    public async Task<ServiceClientResultV1> RotateAsync(
        InstitutionId institutionId,
        string serviceClientId,
        string credentialReference,
        DateTimeOffset credentialExpiresAt,
        DateTimeOffset? previousValidUntil,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);

        ServiceClientRecord record = await LoadActiveAsync(serviceClientId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string reference = Normalise(() => ServiceClientRules.NormaliseReference(credentialReference));
        Normalise(() =>
        {
            ServiceClientRules.ValidateExpiry(credentialExpiresAt, now);
            return true;
        });

        record.PreviousCredentialReference = record.CredentialReference;
        record.PreviousValidUntil = previousValidUntil ?? now;
        record.CredentialReference = reference;
        record.CredentialExpiresAt = credentialExpiresAt;
        record.UpdatedAt = now;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToResult(record);
    }

    public async Task<ServiceClientResultV1> RevokeAsync(
        InstitutionId institutionId,
        string serviceClientId,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ServiceClientAdministrationException(
                ServiceClientContractV1.InvalidRequest, "A revocation reason is required.");
        }

        ServiceClientRecord record = await LoadAsync(serviceClientId, cancellationToken).ConfigureAwait(false);
        if (record.Status != RecordStatus.Active)
        {
            throw new ServiceClientAdministrationException(
                ServiceClientContractV1.AlreadyRevoked, "The service client is not active.");
        }

        record.Status = RecordStatus.Disabled;
        record.UpdatedAt = _timeProvider.GetUtcNow();
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToResult(record);
    }

    public async Task<ServiceClientResultV1[]> ListAsync(CancellationToken cancellationToken) =>
        await _context.ServiceClients
            .OrderBy(client => client.ClientName)
            .Select(client => ToResult(client))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);

    public async Task<ServiceClientResultV1> GetAsync(string serviceClientId, CancellationToken cancellationToken) =>
        ToResult(await LoadAsync(serviceClientId, cancellationToken).ConfigureAwait(false));

    public async Task<bool> IsUsableAsync(AuthenticatedActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        ServiceClientRecord? record = await _context.ServiceClients
            .SingleOrDefaultAsync(client => client.ServiceClientId == actor.Id.Value, cancellationToken)
            .ConfigureAwait(false);

        return record is not null
            && record.InstitutionId == actor.InstitutionId.Value
            && ServiceClientRules.IsUsable(record.Status.ToString(), record.CredentialExpiresAt, _timeProvider.GetUtcNow());
    }

    private async Task<ServiceClientRecord> LoadActiveAsync(string serviceClientId, CancellationToken cancellationToken)
    {
        ServiceClientRecord record = await LoadAsync(serviceClientId, cancellationToken).ConfigureAwait(false);
        if (record.Status != RecordStatus.Active)
        {
            throw new ServiceClientAdministrationException(
                ServiceClientContractV1.AlreadyRevoked, "The service client is not active.");
        }

        return record;
    }

    private async Task<ServiceClientRecord> LoadAsync(string serviceClientId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceClientId);

        ServiceClientRecord? record = await _context.ServiceClients
            .SingleOrDefaultAsync(client => client.ServiceClientId == serviceClientId, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            throw new ServiceClientAdministrationException(
                ServiceClientContractV1.NotFound, "The service client is not available.");
        }

        return record;
    }

    private static T Normalise<T>(Func<T> normalise)
    {
        try
        {
            return normalise();
        }
        catch (ArgumentException exception)
        {
            throw new ServiceClientAdministrationException(ServiceClientContractV1.InvalidRequest, exception.Message);
        }
    }

    private static ServiceClientResultV1 ToResult(ServiceClientRecord record) => new()
    {
        ServiceClientId = record.ServiceClientId,
        ClientName = record.ClientName,
        Audience = record.Audience,
        Status = record.Status == RecordStatus.Active ? ServiceClientContractV1.StatusActive : ServiceClientContractV1.StatusRevoked,
        CredentialExpiresAt = record.CredentialExpiresAt,
        UpdatedAt = record.UpdatedAt,
    };
}

/// <summary>Stable refusal with a contract code. It never carries credential material.</summary>
public sealed class ServiceClientAdministrationException : Exception
{
    public ServiceClientAdministrationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
