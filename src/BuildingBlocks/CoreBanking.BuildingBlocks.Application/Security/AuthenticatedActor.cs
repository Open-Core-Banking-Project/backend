using System;
using System.Collections.Generic;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.BuildingBlocks.Application.Security;

/// <summary>Kind of actor presented to a service. Decision: ADR-0008.</summary>
public enum ActorType
{
    Human = 0,
    Service = 1,
    Partner = 2,
}

/// <summary>Strength of the authentication behind the current request. Decision: ADR-0008.</summary>
public enum AuthenticationStrength
{
    None = 0,
    SingleFactor = 1,
    MultiFactor = 2,
    StepUp = 3,
}

/// <summary>
/// The authenticated actor and its trusted context, as resolved by the service from
/// validated authentication material. Never constructed from client-supplied role,
/// tenant or permission claims.
/// Decision: docs/adr/ADR-0008-authentication-provider-token-model-and-service-identity.md
/// </summary>
public sealed record AuthenticatedActor
{
    public required ActorId Id { get; init; }

    public required ActorType Type { get; init; }

    public required InstitutionId InstitutionId { get; init; }

    public string? SessionId { get; init; }

    public string? DeviceId { get; init; }

    /// <summary>Entitlement version carried by the token, compared with the current version.</summary>
    public required long EntitlementVersion { get; init; }

    public required AuthenticationStrength Strength { get; init; }

    /// <summary>Context kinds already validated for this actor, such as device or step-up.</summary>
    public IReadOnlySet<string> ValidatedContext { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>A permission an operation requires, with the conditions attached to it.</summary>
public sealed record PermissionRequirement
{
    private static readonly HashSet<ActorType> HumanOnly = new() { ActorType.Human };

    public required PermissionIdentifier Permission { get; init; }

    /// <summary>Actor kinds permitted to satisfy this requirement. Human by default.</summary>
    public IReadOnlySet<ActorType> AllowedActorTypes { get; init; } = HumanOnly;

    /// <summary>Whether the operation requires step-up authentication.</summary>
    public bool RequiresStepUp { get; init; }

    /// <summary>Whether the operation is a restricted approval, which triggers segregation of duties.</summary>
    public bool IsRestrictedApproval { get; init; }

    public static PermissionRequirement For(PermissionIdentifier permission) => new() { Permission = permission };
}
