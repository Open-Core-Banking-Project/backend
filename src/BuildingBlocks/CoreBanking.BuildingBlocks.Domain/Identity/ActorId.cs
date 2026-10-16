using System;

namespace CoreBanking.BuildingBlocks.Domain.Identity;

/// <summary>
/// Strongly typed identifier for an authenticated actor, human or service.
/// Decision: docs/adr/ADR-0008-authentication-provider-token-model-and-service-identity.md
/// </summary>
public sealed record ActorId
{
    private ActorId(string value) => Value = value;

    public string Value { get; }

    public static ActorId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An actor identifier is required.", nameof(value));
        }

        return new ActorId(value.Trim());
    }

    public override string ToString() => Value;
}
