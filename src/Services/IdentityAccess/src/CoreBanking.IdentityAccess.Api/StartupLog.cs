using System;
using Microsoft.Extensions.Logging;

namespace CoreBanking.IdentityAccess.Api;

/// <summary>
/// Source-generated startup logs. The analyzer policy requires LoggerMessage delegates
/// rather than interpolated log calls (CA1848), and the seeding record is the startup
/// evidence that the reference protected flow can be exercised.
/// </summary>
internal static partial class StartupLog
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Development actor resolution is disabled outside Development; protected endpoints refuse every request until the identity provider is configured.")]
    public static partial void DevelopmentActorResolutionDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Seeded development identities in institution {InstitutionId}: {AdministratorActorId} (entitlement version {AdministratorVersion}) and {OperatorActorId} (entitlement version {OperatorVersion})")]
    public static partial void DevelopmentIdentitiesSeeded(
        ILogger logger,
        string institutionId,
        string administratorActorId,
        long administratorVersion,
        string operatorActorId,
        long operatorVersion);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Effective permissions are read from identity_access_db; development identities are not seeded.")]
    public static partial void RelationalEntitlementStoreInUse(ILogger logger);
    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "The reference producer endpoint is enabled; events are published by the worker host, never by this API.")]
    public static partial void ReferenceProducerEnabled(ILogger logger);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Warning,
        Message = "Readiness check failed for the required Identity Access database.")]
    public static partial void IdentityAccessDatabaseReadinessFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Information,
        Message = "Keycloak JWT authentication is enabled for authority {Authority} and audience {Audience}.")]
    public static partial void KeycloakAuthenticationEnabled(ILogger logger, string authority, string audience);
}
