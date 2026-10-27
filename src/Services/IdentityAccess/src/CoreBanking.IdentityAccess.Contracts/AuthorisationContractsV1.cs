using System;
using System.Collections.Generic;

namespace CoreBanking.IdentityAccess.Contracts;

/// <summary>Stable values used by the version 1 Identity and Access contracts.</summary>
public static class IdentityAccessContractV1
{
    public const int SchemaVersion = 1;
    public const string EntitlementVersionAdvancedEventType = "entitlement_version_advanced";
    public const string EntitlementEventFamily = "entitlements";
}

/// <summary>Actor kinds are explicit on the wire; service actors never inherit human grants.</summary>
public static class ActorTypeV1
{
    public const string Customer = "customer";
    public const string Staff = "staff";
    public const string Agent = "agent";
    public const string CorporateUser = "corporate_user";
    public const string Administrator = "administrator";
    public const string Service = "service";
    public const string Partner = "partner";
}

/// <summary>Authentication assurance values carried without provider-specific method details.</summary>
public static class AuthenticationStrengthV1
{
    public const string None = "none";
    public const string SingleFactor = "single_factor";
    public const string MultiFactor = "multi_factor";
    public const string StepUp = "step_up";
}

/// <summary>Stable decision values for transport-neutral authorisation responses.</summary>
public static class AuthorisationOutcomeV1
{
    public const string Allowed = "allowed";
    public const string Denied = "denied";
}

/// <summary>
/// Trusted institution context for a service operation. It is resolved from validated
/// authentication or service context, never accepted as proof from an ordinary request body.
/// </summary>
public sealed record TenantContextV1
{
    public int SchemaVersion { get; init; } = IdentityAccessContractV1.SchemaVersion;

    public required string InstitutionId { get; init; }
}

/// <summary>
/// Data-minimised authenticated actor context. Permissions and role names are deliberately
/// absent; consumers resolve them from <see cref="EntitlementSnapshotV1"/>.
/// </summary>
public sealed record ActorContextV1
{
    public int SchemaVersion { get; init; } = IdentityAccessContractV1.SchemaVersion;

    public required string ActorId { get; init; }

    public required string ActorType { get; init; }

    public required string InstitutionId { get; init; }

    public string? SessionId { get; init; }

    public string? DeviceId { get; init; }

    public required long EntitlementVersion { get; init; }

    public required string AuthenticationStrength { get; init; }

    public IReadOnlyList<string> ValidatedContext { get; init; } = Array.Empty<string>();
}

/// <summary>One protected operation's stable capability and security conditions.</summary>
public sealed record PermissionRequirementV1
{
    public int SchemaVersion { get; init; } = IdentityAccessContractV1.SchemaVersion;

    public required string Permission { get; init; }

    public required IReadOnlyList<string> AllowedActorTypes { get; init; }

    public bool RequiresStepUp { get; init; }

    public bool IsRestrictedApproval { get; init; }
}

/// <summary>The contextual resource dimensions presented to the owning business service.</summary>
public sealed record ResourceScopeV1
{
    public required string InstitutionId { get; init; }

    public string? LegalEntityId { get; init; }

    public string? RegionId { get; init; }

    public string? BranchId { get; init; }

    public string? ChannelId { get; init; }

    public string? CustomerId { get; init; }

    public string? AccountId { get; init; }

    public string? MandateId { get; init; }

    public string? Currency { get; init; }

    public decimal? Amount { get; init; }

    public string? DeviceId { get; init; }
}

/// <summary>Scope attached to an effective permission grant.</summary>
public sealed record GrantScopeV1
{
    public required string InstitutionId { get; init; }

    public string? LegalEntityId { get; init; }

    public string? RegionId { get; init; }

    public string? BranchId { get; init; }

    public string? ChannelId { get; init; }

    public string? CustomerId { get; init; }

    public string? AccountId { get; init; }

    public string? MandateId { get; init; }

    public string? Currency { get; init; }

    public decimal? MaximumAmount { get; init; }

    public string? DeviceId { get; init; }

    public string? MinimumAuthenticationStrength { get; init; }
}

/// <summary>An effective permission and scope, without role names or internal persistence keys.</summary>
public sealed record PermissionGrantV1
{
    public required string Permission { get; init; }

    public required GrantScopeV1 Scope { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ValidTo { get; init; }
}

/// <summary>Audience-restricted request for the current actor/institution entitlement snapshot.</summary>
public sealed record EntitlementSnapshotRequestV1
{
    public int SchemaVersion { get; init; } = IdentityAccessContractV1.SchemaVersion;

    public required string ActorId { get; init; }

    public required string InstitutionId { get; init; }

    /// <summary>The minimum version already observed by the caller; a lower response is rejected.</summary>
    public required long MinimumVersion { get; init; }
}

/// <summary>
/// Complete effective permissions for one actor in one institution at one monotonic version.
/// Consumers deny when this snapshot is incomplete, stale or version-mismatched.
/// </summary>
public sealed record EntitlementSnapshotV1
{
    public int SchemaVersion { get; init; } = IdentityAccessContractV1.SchemaVersion;

    public required string ActorId { get; init; }

    public required string InstitutionId { get; init; }

    public required long Version { get; init; }

    public required bool IsComplete { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public required IReadOnlyList<PermissionGrantV1> Grants { get; init; }
}

/// <summary>Safe, transport-neutral result of one permission evaluation.</summary>
public sealed record AuthorisationDecisionV1
{
    public int SchemaVersion { get; init; } = IdentityAccessContractV1.SchemaVersion;

    public required string Decision { get; init; }

    public required string Permission { get; init; }

    public string? ReasonCode { get; init; }

    public string? SafeDetail { get; init; }

    public required long EvaluatedEntitlementVersion { get; init; }
}

/// <summary>
/// Durable invalidation fact. Consumers apply it idempotently, reject version regression and
/// fetch a fresh snapshot when <see cref="PreviousVersion"/> does not match local state.
/// </summary>
public sealed record EntitlementVersionAdvancedV1
{
    public required string ActorId { get; init; }

    public required string InstitutionId { get; init; }

    public required long PreviousVersion { get; init; }

    public required long NewVersion { get; init; }

    public required string ChangeReasonCode { get; init; }

    public required string SourceReference { get; init; }
}
