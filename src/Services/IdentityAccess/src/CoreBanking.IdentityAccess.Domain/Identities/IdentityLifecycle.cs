using System;

namespace CoreBanking.IdentityAccess.Domain.Identities;

/// <summary>Explicit platform identity kinds required by IAM-001.</summary>
public enum IdentityKind
{
    Customer = 1,
    Staff = 2,
    Agent = 3,
    CorporateUser = 4,
    Administrator = 5,
    Service = 6,
}

public enum IdentityState
{
    Pending = 0,
    Active = 1,
    Locked = 2,
    Suspended = 3,
    Disabled = 4,
    Closed = 5,
}

/// <summary>Pure lifecycle rules; persistence and provider operations remain outside the domain.</summary>
public static class IdentityLifecycle
{
    public static IdentityKind ParseKind(string value) => value?.Trim() switch
    {
        "customer" => IdentityKind.Customer,
        "staff" => IdentityKind.Staff,
        "agent" => IdentityKind.Agent,
        "corporate_user" => IdentityKind.CorporateUser,
        "administrator" => IdentityKind.Administrator,
        "service" => IdentityKind.Service,
        _ => throw new ArgumentException("The identity type is not supported.", nameof(value)),
    };

    public static string ToContractValue(IdentityKind kind) => kind switch
    {
        IdentityKind.Customer => "customer",
        IdentityKind.Staff => "staff",
        IdentityKind.Agent => "agent",
        IdentityKind.CorporateUser => "corporate_user",
        IdentityKind.Administrator => "administrator",
        IdentityKind.Service => "service",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The identity type is not supported."),
    };

    public static IdentityState ParseState(string value) => value?.Trim() switch
    {
        "pending" => IdentityState.Pending,
        "active" => IdentityState.Active,
        "locked" => IdentityState.Locked,
        "suspended" => IdentityState.Suspended,
        "disabled" => IdentityState.Disabled,
        "closed" => IdentityState.Closed,
        _ => throw new ArgumentException("The identity status is not supported.", nameof(value)),
    };

    public static bool CanTransition(IdentityState current, IdentityState requested) => (current, requested) switch
    {
        (IdentityState.Pending, IdentityState.Active) => true,
        (IdentityState.Active, IdentityState.Locked) => true,
        (IdentityState.Active, IdentityState.Suspended) => true,
        (IdentityState.Active, IdentityState.Disabled) => true,
        (IdentityState.Locked, IdentityState.Active) => true,
        (IdentityState.Suspended, IdentityState.Active) => true,
        (IdentityState.Disabled, IdentityState.Closed) => true,
        _ => false,
    };
}
