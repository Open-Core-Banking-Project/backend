using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.IdentityAccess.Application.Authorisation;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests;

/// <summary>
/// The refusal contract: missing authentication is 401, every other refusal is 403 with a
/// unique stable code, and an allowed decision cannot be projected as a problem.
/// Decision: epic 03 API requirements.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AuthorisationProblemTests
{
    [Fact]
    public void Missing_authentication_maps_to_401()
    {
        AuthorisationDecision decision = AuthorisationDecision.Denied(DenialReason.MissingAuthentication, "no actor");

        AuthorisationProblem problem = AuthorisationProblem.From(decision, "correlation-1");

        Assert.Equal(401, problem.Status);
        Assert.Equal("auth.missing_authentication", problem.Code);
        Assert.Equal("correlation-1", problem.CorrelationId);
    }

    [Fact]
    public void Every_other_denial_maps_to_403_with_a_unique_stable_code()
    {
        HashSet<string> codes = new(StringComparer.Ordinal);
        List<DenialReason> checkedReasons = [];

        foreach (DenialReason reason in Enum.GetValues<DenialReason>())
        {
            // Missing authentication is the only refusal that is 401, and it has its own test.
            if (reason is DenialReason.None or DenialReason.MissingAuthentication)
            {
                continue;
            }

            AuthorisationProblem problem = AuthorisationProblem.From(AuthorisationDecision.Denied(reason, "detail"), "correlation-2");

            Assert.Equal(403, problem.Status);
            Assert.StartsWith("auth.", problem.Code, StringComparison.Ordinal);
            Assert.True(codes.Add(problem.Code), $"Duplicate code '{problem.Code}' for {reason}.");

            checkedReasons.Add(reason);
        }

        DenialReason[] expected = Enum.GetValues<DenialReason>()
            .Where(reason => reason is not (DenialReason.None or DenialReason.MissingAuthentication))
            .Order()
            .ToArray();

        Assert.Equal(expected, checkedReasons.Order().ToArray());
    }

    [Fact]
    public void Allowed_decision_cannot_be_projected_as_a_problem()
    {
        AuthorisationDecision allowed = AuthorisationDecision.Allowed(PermissionIdentifier.Parse("platform.foundation.execute"));

        Assert.Throws<ArgumentException>(() => AuthorisationProblem.From(allowed, "correlation-3"));
    }

    [Fact]
    public void Detail_defaults_when_the_policy_supplies_none()
    {
        AuthorisationProblem problem = AuthorisationProblem.From(AuthorisationDecision.Denied(DenialReason.ScopeMismatch), "correlation-4");

        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }
}
