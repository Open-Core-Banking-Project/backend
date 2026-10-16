using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Application.Authorisation;
using Microsoft.AspNetCore.Http;

namespace CoreBanking.IdentityAccess.Api.Authorisation;

/// <summary>
/// Declares a permission for one protected endpoint and enforces it before the handler runs.
/// The requested institution and branch are read from the request as the target scope, and the
/// policy refuses any request whose target does not match the authenticated context, so
/// client-supplied context can never widen access.
/// Decision: ADR-0009, epic 03 API requirements.
/// </summary>
public sealed class ProtectedOperationFilter : IEndpointFilter
{
    public const string ActorItemKey = "authorisation.actor";

    public const string CorrelationItemKey = "authorisation.correlationId";

    private const string AuthenticationHeaderName = "X-Development-Actor";
    private const string CorrelationHeaderName = "X-Correlation-Id";
    private const string InstitutionHeaderName = "X-Institution-Id";
    private const string BranchHeaderName = "X-Branch-Id";

    private readonly AuthorisationService _authorisation;
    private readonly IActorContextResolver _actorContextResolver;
    private readonly PermissionRequirement _requirement;
    private readonly string _operationName;

    public ProtectedOperationFilter(
        AuthorisationService authorisation,
        IActorContextResolver actorContextResolver,
        PermissionRequirement requirement,
        string operationName)
    {
        _authorisation = authorisation ?? throw new ArgumentNullException(nameof(authorisation));
        _actorContextResolver = actorContextResolver ?? throw new ArgumentNullException(nameof(actorContextResolver));
        _requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        _operationName = operationName;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpContext http = context.HttpContext;
        string correlationId = ReadHeader(http, CorrelationHeaderName) ?? Guid.NewGuid().ToString("N");
        AuthenticatedActor? actor = _actorContextResolver.Resolve(ReadHeader(http, AuthenticationHeaderName));

        AuthorisationRequest request = new()
        {
            Requirement = _requirement,
            Scope = BuildRequestedScope(http),
            Actor = actor,
        };

        AuthorisationDecision decision = _authorisation.Authorise(request, _operationName, correlationId);
        if (!decision.IsAllowed)
        {
            AuthorisationProblem problem = AuthorisationProblem.From(decision, correlationId);
            Dictionary<string, object?> extensions = new(StringComparer.Ordinal)
            {
                ["code"] = problem.Code,
                ["correlationId"] = problem.CorrelationId,
            };

            return Results.Problem(
                detail: problem.Detail,
                statusCode: problem.Status,
                title: problem.Title,
                extensions: extensions);
        }

        http.Items[ActorItemKey] = actor;
        http.Items[CorrelationItemKey] = correlationId;

        return await next(context);
    }

    private static ResourceScope BuildRequestedScope(HttpContext http)
    {
        string? institution = ReadHeader(http, InstitutionHeaderName);
        if (institution is null)
        {
            return ResourceScope.None;
        }

        return new ResourceScope
        {
            InstitutionId = InstitutionId.From(institution),
            BranchId = ReadHeader(http, BranchHeaderName),
        };
    }

    private static string? ReadHeader(HttpContext http, string name)
    {
        if (!http.Request.Headers.TryGetValue(name, out Microsoft.Extensions.Primitives.StringValues values))
        {
            return null;
        }

        string? value = values.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
