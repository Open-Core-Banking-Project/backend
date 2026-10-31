using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.IntegrationHub.Application.ProviderAdministration;
using CoreBanking.IntegrationHub.Domain.ProviderAdministration;
using CoreBanking.IntegrationHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IntegrationHub.Infrastructure.ProviderAdministration;

public sealed class ProviderAdministrationService : IProviderAdministrationService
{
    private readonly IntegrationHubDbContext _database;
    private readonly TimeProvider _timeProvider;

    public ProviderAdministrationService(IntegrationHubDbContext database, TimeProvider timeProvider)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CredentialReferenceResult> RegisterCredentialReferenceAsync(RegisterCredentialReferenceCommand command, CancellationToken cancellationToken)
    {
        ValidateEvidence(command.ActorId, command.Reason, command.CorrelationId);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string id = Guid.NewGuid().ToString("N");
        CredentialReference reference = CredentialReference.Register(id, command.InstitutionId, command.Environment, command.ProviderCode, command.Kind, command.ReferenceName, command.Owner, now, command.ExpiresAt);
        EnsureActiveBoundary(command.InstitutionId);
        bool duplicate = await _database.CredentialReferences.AnyAsync(record =>
            record.InstitutionId == command.InstitutionId && record.Environment == reference.Environment
            && record.ProviderCode == reference.ProviderCode && record.ReferenceName == reference.ReferenceName
            && record.Status == "active", cancellationToken);
        if (duplicate) throw new InvalidOperationException("credential_reference_conflict");
        _database.CredentialReferences.Add(ToRecord(reference, id, command.ActorId, command.Reason, command.CorrelationId, now));
        await _database.SaveChangesAsync(cancellationToken);
        return new CredentialReferenceResult(id, 1, "active", command.ExpiresAt);
    }

    public async Task<CredentialReferenceResult> RotateCredentialReferenceAsync(RotateCredentialReferenceCommand command, CancellationToken cancellationToken)
    {
        ValidateEvidence(command.ActorId, command.Reason, command.CorrelationId);
        EnsureActiveBoundary(command.InstitutionId);
        CredentialReferenceRecord current = await _database.CredentialReferences
            .Where(record => record.CredentialReferenceId == command.CredentialReferenceId && record.Status == "active")
            .OrderByDescending(record => record.Version).SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("credential_reference_not_found");
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string recordId = Guid.NewGuid().ToString("N");
        CredentialReference rotated = CredentialReference.Register(current.CredentialReferenceId, command.InstitutionId, current.Environment, current.ProviderCode,
            Enum.Parse<CredentialReferenceKind>(current.Kind), command.ReferenceName, current.Owner, now, command.ExpiresAt, current.Version + 1, current.RecordId);
        current.Status = "superseded";
        _database.CredentialReferences.Add(ToRecord(rotated, recordId, command.ActorId, command.Reason, command.CorrelationId, now));
        await _database.SaveChangesAsync(cancellationToken);
        return new CredentialReferenceResult(rotated.Id, rotated.Version, "active", rotated.ExpiresAt);
    }

    public async Task<ApiClientResult> RegisterApiClientAsync(RegisterApiClientCommand command, CancellationToken cancellationToken)
    {
        ValidateEvidence(command.ActorId, command.Reason, command.CorrelationId);
        EnsureActiveBoundary(command.InstitutionId);
        string id = Guid.NewGuid().ToString("N");
        ApiClient client = ApiClient.Register(id, command.InstitutionId, command.Environment, command.ClientIdentifier, command.DisplayName, command.AllowedScopes, command.AllowedNetworkRanges);
        if (await _database.ApiClients.AnyAsync(record => record.Environment == client.Environment && record.ClientIdentifier == client.ClientIdentifier, cancellationToken))
            throw new InvalidOperationException("api_client_conflict");
        _database.ApiClients.Add(new ApiClientRecord
        {
            ApiClientId = id,
            InstitutionId = command.InstitutionId,
            Environment = client.Environment,
            ClientIdentifier = client.ClientIdentifier,
            DisplayName = client.DisplayName,
            AllowedScopesJson = JsonSerializer.Serialize(client.AllowedScopes),
            AllowedNetworkRangesJson = JsonSerializer.Serialize(client.AllowedNetworkRanges),
            Status = "active",
            ActorId = command.ActorId,
            Reason = command.Reason.Trim(),
            CorrelationId = command.CorrelationId,
            CreatedAt = _timeProvider.GetUtcNow(),
        });
        await _database.SaveChangesAsync(cancellationToken);
        return new ApiClientResult(id, client.ClientIdentifier, "active");
    }

    public async Task<ApiClientResult> RevokeApiClientAsync(RevokeApiClientCommand command, CancellationToken cancellationToken)
    {
        ValidateEvidence(command.ActorId, command.Reason, command.CorrelationId);
        EnsureActiveBoundary(command.InstitutionId);
        ApiClientRecord client = await _database.ApiClients.SingleOrDefaultAsync(record => record.ApiClientId == command.ApiClientId, cancellationToken)
            ?? throw new InvalidOperationException("api_client_not_found");
        if (client.Status != "revoked")
        {
            client.Status = "revoked";
            client.RevokedAt = _timeProvider.GetUtcNow();
            client.RevokedBy = command.ActorId;
            client.RevocationReason = command.Reason.Trim();
            await _database.SaveChangesAsync(cancellationToken);
        }
        return new ApiClientResult(client.ApiClientId, client.ClientIdentifier, client.Status);
    }

    private void EnsureActiveBoundary(string institutionId)
    {
        InstitutionBoundaryRecord? boundary = _database.InstitutionBoundaries.SingleOrDefault(record => record.InstitutionId == institutionId);
        if (boundary?.Status != "active") throw new InvalidOperationException("institution_not_active");
    }

    private static CredentialReferenceRecord ToRecord(CredentialReference value, string recordId, string actorId, string reason, string correlationId, DateTimeOffset createdAt) => new()
    {
        RecordId = recordId,
        CredentialReferenceId = value.Id,
        InstitutionId = value.InstitutionId,
        Environment = value.Environment,
        ProviderCode = value.ProviderCode,
        Kind = value.Kind.ToString(),
        ReferenceName = value.ReferenceName,
        Owner = value.Owner,
        Version = value.Version,
        Status = "active",
        ExpiresAt = value.ExpiresAt,
        PreviousRecordId = value.PreviousVersionId,
        ActorId = actorId,
        Reason = reason.Trim(),
        CorrelationId = correlationId,
        CreatedAt = createdAt,
    };

    private static void ValidateEvidence(string actorId, string reason, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (reason.Trim().Length > 512) throw new ArgumentOutOfRangeException(nameof(reason));
    }
}
