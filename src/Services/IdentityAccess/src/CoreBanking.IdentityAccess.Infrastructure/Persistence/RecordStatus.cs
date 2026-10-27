namespace CoreBanking.IdentityAccess.Infrastructure.Persistence;

/// <summary>Lifecycle of a stored identity, service client or role.</summary>
public enum RecordStatus
{
    Pending = 0,
    Active = 1,
    Suspended = 2,
    Locked = 3,
    Disabled = 4,
    Closed = 5,
    Archived = 6,
}

/// <summary>Outcome recorded for an authentication attempt.</summary>
public enum AuthenticationOutcome
{
    Succeeded = 1,
    Failed = 2,
}
