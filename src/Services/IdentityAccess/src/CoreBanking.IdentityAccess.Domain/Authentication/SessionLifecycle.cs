namespace CoreBanking.IdentityAccess.Domain.Authentication;

/// <summary>Local security state for a provider-backed session. Token material is never retained.</summary>
public enum SessionLifecycleState
{
    Active,
    Refreshed,
    Revoked,
    Compromised,
    Expired,
}

public static class SessionLifecycle
{
    public static bool CanRefresh(SessionLifecycleState state) =>
        state is SessionLifecycleState.Active or SessionLifecycleState.Refreshed;

    public static bool CanRevoke(SessionLifecycleState state) =>
        state is SessionLifecycleState.Active or SessionLifecycleState.Refreshed or SessionLifecycleState.Expired;
}
