using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.IdentityAccess.Domain.Authentication;

namespace CoreBanking.IdentityAccess.Application.Devices;

/// <summary>
/// Starts a registration: the client supplies only the public-key thumbprint and untrusted display metadata.
/// The registration stays `pending_step_up` until a fresh step-up for that purpose is consumed.
/// Decision: P1-E02-D11.
/// </summary>
public sealed record StartDeviceRegistrationCommand(
    AuthenticatedActor Actor,
    string IdentityId,
    string KeyThumbprint,
    DevicePlatform Platform,
    string? DisplayName,
    string CorrelationId);

/// <summary>Completes a registration with the step-up evidence the operation consumed.</summary>
public sealed record CompleteDeviceRegistrationCommand(
    AuthenticatedActor Actor,
    string DeviceId,
    string CorrelationId);

public sealed record ListDevicesQuery(
    AuthenticatedActor Actor,
    string IdentityId,
    bool OtherIdentityRequested);

public sealed record SuspendDeviceCommand(
    AuthenticatedActor Actor,
    string DeviceId,
    string Reason,
    string CorrelationId);

/// <summary>Revokes one device and, in the same transaction, every session bound to it.</summary>
public sealed record RevokeDeviceCommand(
    AuthenticatedActor Actor,
    string DeviceId,
    string Reason,
    bool Administrative,
    string CorrelationId);

public sealed record TrustedDeviceView(
    string DeviceId,
    string IdentityId,
    string KeyThumbprint,
    string PlatformCode,
    string? DisplayName,
    string TrustStateCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? TrustedAt,
    DateTimeOffset? TrustExpiresAt,
    DateTimeOffset? RevokedAt,
    string? RevocationReason,
    bool IsTrustedNow,
    long ConcurrencyVersion);

public sealed record DeviceRevocationView(
    string DeviceId,
    string TrustStateCode,
    int RevokedSessionCount,
    bool Replayed);

public interface ITrustedDeviceService
{
    Task<TrustedDeviceView> StartRegistrationAsync(StartDeviceRegistrationCommand command, CancellationToken cancellationToken);

    Task<TrustedDeviceView> CompleteRegistrationAsync(CompleteDeviceRegistrationCommand command, CancellationToken cancellationToken);

    Task<IReadOnlyList<TrustedDeviceView>> ListAsync(ListDevicesQuery query, CancellationToken cancellationToken);

    Task<TrustedDeviceView> SuspendAsync(SuspendDeviceCommand command, CancellationToken cancellationToken);

    Task<DeviceRevocationView> RevokeAsync(RevokeDeviceCommand command, CancellationToken cancellationToken);
}

/// <summary>Trust duration policy. Approved defaults: 180 days standard, 30 days privileged. Decision: P1-E02-D11.</summary>
public sealed record TrustDurationPolicy
{
    public TimeSpan Standard { get; init; } = TimeSpan.FromDays(180);

    public TimeSpan Privileged { get; init; } = TimeSpan.FromDays(30);

    public static TrustDurationPolicy Default { get; } = new();

    public void Validate()
    {
        if (Standard <= TimeSpan.Zero || Privileged <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Device trust durations must be positive.");
        }
    }
}

public sealed class TrustedDeviceException : Exception
{
    public TrustedDeviceException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
