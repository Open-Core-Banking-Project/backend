using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IntegrationHub.Domain.ProviderAdministration;

namespace CoreBanking.IntegrationHub.Application.ProviderAdministration;

public static class ProviderAdministrationPermissions
{
    public static readonly PermissionIdentifier CredentialReferenceManage = PermissionIdentifier.Parse("integration.credential_reference.manage");
    public static readonly PermissionIdentifier ApiClientManage = PermissionIdentifier.Parse("integration.api_client.manage");
    public static readonly PermissionRequirement CredentialReferenceManageRequirement = new() { Permission = CredentialReferenceManage, RequiresStepUp = true };
    public static readonly PermissionRequirement ApiClientManageRequirement = new() { Permission = ApiClientManage, RequiresStepUp = true };
}

public sealed record RegisterCredentialReferenceCommand(
    string InstitutionId,
    string Environment,
    string ProviderCode,
    CredentialReferenceKind Kind,
    string ReferenceName,
    string Owner,
    DateTimeOffset? ExpiresAt,
    string ActorId,
    string Reason,
    string CorrelationId);

public sealed record RotateCredentialReferenceCommand(
    string InstitutionId,
    string CredentialReferenceId,
    string ReferenceName,
    DateTimeOffset? ExpiresAt,
    string ActorId,
    string Reason,
    string CorrelationId);

public sealed record RegisterApiClientCommand(
    string InstitutionId,
    string Environment,
    string ClientIdentifier,
    string DisplayName,
    IReadOnlyList<string> AllowedScopes,
    IReadOnlyList<string> AllowedNetworkRanges,
    string ActorId,
    string Reason,
    string CorrelationId);

public sealed record RevokeApiClientCommand(
    string InstitutionId,
    string ApiClientId,
    string ActorId,
    string Reason,
    string CorrelationId);

public sealed record CredentialReferenceResult(string Id, int Version, string Status, DateTimeOffset? ExpiresAt);
public sealed record ApiClientResult(string Id, string ClientIdentifier, string Status);

public interface IProviderAdministrationService
{
    Task<CredentialReferenceResult> RegisterCredentialReferenceAsync(RegisterCredentialReferenceCommand command, CancellationToken cancellationToken);
    Task<CredentialReferenceResult> RotateCredentialReferenceAsync(RotateCredentialReferenceCommand command, CancellationToken cancellationToken);
    Task<ApiClientResult> RegisterApiClientAsync(RegisterApiClientCommand command, CancellationToken cancellationToken);
    Task<ApiClientResult> RevokeApiClientAsync(RevokeApiClientCommand command, CancellationToken cancellationToken);
}
