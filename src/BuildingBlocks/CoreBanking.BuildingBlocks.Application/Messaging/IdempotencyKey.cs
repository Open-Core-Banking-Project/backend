using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CoreBanking.BuildingBlocks.Application.Messaging;

/// <summary>
/// A scoped idempotency key: institution, operation and initiating identity or client. The same
/// key with an equivalent request returns the original outcome; the same key with a materially
/// different request is a stable conflict. Never used as an arbitrary cache timeout.
/// Decision: ADR-0007.
/// </summary>
public sealed record IdempotencyKey
{
    private IdempotencyKey(string institutionId, string operation, string initiator, string key)
    {
        InstitutionId = institutionId;
        Operation = operation;
        Initiator = initiator;
        Value = key;
    }

    public string InstitutionId { get; }

    public string Operation { get; }

    public string Initiator { get; }

    public string Value { get; }

    public static IdempotencyKey Create(string institutionId, string operation, string initiator, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(initiator);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return new IdempotencyKey(institutionId.Trim(), operation.Trim(), initiator.Trim(), key.Trim());
    }

    /// <summary>A stable fingerprint of the request body, used to detect a materially different request.</summary>
    public static string Fingerprint(string requestBody)
    {
        ArgumentNullException.ThrowIfNull(requestBody);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(requestBody));
        return Convert.ToHexStringLower(hash);
    }

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "{0}/{1}/{2}/{3}", InstitutionId, Operation, Initiator, Value);
}

/// <summary>The outcome of an idempotent command.</summary>
public enum IdempotencyOutcomeKind
{
    /// <summary>The command ran and its result was recorded.</summary>
    Executed = 1,

    /// <summary>The key was already used with an equivalent request; the recorded result is returned.</summary>
    Replayed = 2,

    /// <summary>The key was already used with a materially different request.</summary>
    Conflict = 3,

    /// <summary>Another caller holds the key and has not finished yet.</summary>
    InProgress = 4,
}

/// <summary>What happened for one idempotent command, with the recorded result when there is one.</summary>
public sealed record IdempotencyOutcome
{
    public required IdempotencyOutcomeKind Kind { get; init; }

    public string? Result { get; init; }

    public static IdempotencyOutcome Executed(string? result) => new() { Kind = IdempotencyOutcomeKind.Executed, Result = result };

    public static IdempotencyOutcome Replayed(string? result) => new() { Kind = IdempotencyOutcomeKind.Replayed, Result = result };

    public static IdempotencyOutcome Conflict() => new() { Kind = IdempotencyOutcomeKind.Conflict };

    public static IdempotencyOutcome InProgress() => new() { Kind = IdempotencyOutcomeKind.InProgress };
}
