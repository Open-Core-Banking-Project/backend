using System;
using CoreBanking.BuildingBlocks.Application.Security;

namespace CoreBanking.IdentityAccess.Application.Authorisation;

/// <summary>
/// A framework-agnostic problem-details payload for a refusal, so the HTTP layer only has to
/// project it. Codes are stable and never reveal whether a hidden resource exists.
/// Decision: ADR-0009, epic 03 API requirements.
/// </summary>
public sealed record AuthorisationProblem
{
    public required int Status { get; init; }

    public required string Code { get; init; }

    public required string Title { get; init; }

    public required string Detail { get; init; }

    public required string CorrelationId { get; init; }

    public static AuthorisationProblem From(AuthorisationDecision decision, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        if (decision.IsAllowed)
        {
            throw new ArgumentException("An allowed decision is not a problem.", nameof(decision));
        }

        return new AuthorisationProblem
        {
            Status = AuthorisationCodes.StatusFor(decision.Reason),
            Code = AuthorisationCodes.For(decision.Reason),
            Title = AuthorisationCodes.TitleFor(decision.Reason),
            Detail = decision.Detail ?? "The request was refused.",
            CorrelationId = correlationId,
        };
    }
}
