namespace CoreBanking.IdentityAccess.Infrastructure.Persistence;

/// <summary>Lifecycle of a stored identity, service client or role.</summary>
public enum RecordStatus
{
    Active = 1,
    Suspended = 2,
    Archived = 3,
}

/// <summary>Outcome recorded for an authentication attempt.</summary>
public enum AuthenticationOutcome
{
    Succeeded = 1,
    Failed = 2,
}
