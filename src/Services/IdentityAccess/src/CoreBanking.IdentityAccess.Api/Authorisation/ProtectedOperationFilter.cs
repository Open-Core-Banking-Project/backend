using System;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Application.Authentication;
using CoreBanking.IdentityAccess.Application.Authorisation;
using CoreBanking.IdentityAccess.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CoreBanking.IdentityAccess.Api.Authorisation;

/// <summary>
/// Declares a permission for one protected endpoint and enforces it before the handler runs.
/// It resolves the actor, establishes the trusted institution context from that actor, and only
/// then evaluates the policy, so the persistence layer can rely on the context it reads. The
/// requested institution and branch come from the request as the target scope and the policy
/// refuses any mismatch, so client-supplied context can never widen access.
/// Collaborators are resolved per request because the relational entitlement store is scoped to
/// the request's database context.
/// Decision: ADR-0004, ADR-0009, epic 03 API requirements.
/// </summary>
public sealed class ProtectedOperationFilter : IEndpointFilter
{
    public const string ActorItemKey = "authorisation.actor";

    public const string CorrelationItemKey = "authorisation.correlationId";

    private const string AuthenticationHeaderName = "X-Development-Actor";
    private const string CorrelationHeaderName = "X-Correlation-Id";
    /// <summary>The tenant header used by discovery routes that run before an actor exists.</summary>
    public const string InstitutionHeaderName = "X-Institution-Id";
    private const string BranchHeaderName = "X-Branch-Id";

    private readonly PermissionRequirement _requirement;
    private readonly string _operationName;

    public ProtectedOperationFilter(PermissionRequirement requirement, string operationName)
    {
        _requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        _operationName = operationName;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpContext http = context.HttpContext;
        IServiceProvider services = http.RequestServices;
        ITenantContext tenantContext = services.GetRequiredService<ITenantContext>();

        string correlationId = ReadHeader(http, CorrelationHeaderName) ?? Guid.NewGuid().ToString("N");
        AuthenticatedActor? actor = services.GetRequiredService<IActorContextResolver>()
            .Resolve(http.User, ReadHeader(http, AuthenticationHeaderName));

        try
        {
            if (actor is not null)
            {
                // The institution comes from the validated authentication context, never from a request field.
                tenantContext.Establish(actor.InstitutionId);
            }

            // A service token must belong to an active, unexpired service client. This runs before
            // authorisation so an unregistered or expired client can never reach a handler.
            if (actor is { Type: ActorType.Service })
            {
                IServiceClientValidator? validator = services.GetService<IServiceClientValidator>();
                if (validator is not null
                    && !await validator.IsUsableAsync(actor, http.RequestAborted).ConfigureAwait(false))
                {
                    return Results.Problem(
                        detail: "The service identity is not active for this institution.",
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Service identity not permitted",
                        extensions: new System.Collections.Generic.Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["code"] = ServiceClientContractV1.InvalidServiceIdentity,
                            ["correlationId"] = correlationId,
                        });
                }
            }

            AuthorisationRequest request = new()
            {
                Requirement = _requirement,
                Scope = BuildRequestedScope(http),
                Actor = actor,
            };

            AuthorisationDecision decision = await services.GetRequiredService<AuthorisationService>()
                .AuthoriseAsync(request, _operationName, correlationId, http.RequestAborted)
                .ConfigureAwait(false);

            // A fresh, purpose-bound step-up record satisfies the requirement without inventing a
            // token claim. It is consumed once, so the same evidence cannot authorise a second
            // operation. Decision: P1-E02-D08.
            if (!decision.IsAllowed
                && decision.Reason == DenialReason.StepUpRequired
                && _requirement.RequiresStepUp
                && actor is not null)
            {
                IStepUpEvidenceSource? stepUpEvidence = services.GetService<IStepUpEvidenceSource>();
                if (stepUpEvidence is not null
                    && await stepUpEvidence.TryConsumeAsync(actor, _operationName, http.RequestAborted).ConfigureAwait(false))
                {
                    actor = actor with { Strength = AuthenticationStrength.StepUp };
                    tenantContext.Establish(actor.InstitutionId);
                    request = request with { Actor = actor, StepUpSatisfied = true };

                    decision = await services.GetRequiredService<AuthorisationService>()
                        .AuthoriseAsync(request, _operationName + ".step_up", correlationId, http.RequestAborted)
                        .ConfigureAwait(false);
                }
            }

            if (!decision.IsAllowed)
            {
                if (actor is null)
                {
                    http.Response.Headers.WWWAuthenticate = "Bearer";
                }

                AuthorisationProblem problem = AuthorisationProblem.From(decision, correlationId);
                System.Collections.Generic.Dictionary<string, object?> extensions = new(StringComparer.Ordinal)
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
        finally
        {
            tenantContext.Clear();
        }
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
