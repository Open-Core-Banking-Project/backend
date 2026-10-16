using System;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;

namespace CoreBanking.IdentityAccess.Application.Authorisation;

/// <summary>
/// Evaluates a permission requirement and records evidence for the decision. Both allow and
/// deny decisions are recorded, because the decision itself is the evidence. Secrets and
/// personal data are never included.
/// Decision: ADR-0009 and ADR-0010.
/// </summary>
public sealed class AuthorisationService
{
    private const string DecisionAllowed = "allowed";
    private const string DecisionDenied = "denied";
    private const string NoReason = "none";
    private const string ActionAuthorise = "authorise";

    private readonly AuthorisationPolicy _policy;
    private readonly IAuthorisationAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;
    private readonly string _policyVersion;

    public AuthorisationService(
        AuthorisationPolicy policy,
        IAuthorisationAuditWriter auditWriter,
        TimeProvider timeProvider,
        string policyVersion = "phase-0")
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _policyVersion = policyVersion;
    }

    public AuthorisationDecision Authorise(AuthorisationRequest request, string operationName, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        AuthorisationDecision decision = _policy.Evaluate(request);
        _auditWriter.Write(ToEvidence(request, decision, operationName, correlationId));

        return decision;
    }

    private AuthorisationAuditEntry ToEvidence(AuthorisationRequest request, AuthorisationDecision decision, string operationName, string correlationId)
    {
        AuthenticatedActor? actor = request.Actor;

        return new AuthorisationAuditEntry
        {
            AuditEventId = Guid.NewGuid().ToString("N"),
            OccurredAt = _timeProvider.GetUtcNow(),
            ActorId = actor?.Id,
            ActorType = actor?.Type,
            InstitutionId = actor?.InstitutionId,
            Permission = request.Requirement.Permission.Value,
            Action = ActionAuthorise,
            Decision = decision.IsAllowed ? DecisionAllowed : DecisionDenied,
            Reason = decision.IsAllowed ? NoReason : AuthorisationCodes.For(decision.Reason),
            Source = operationName,
            CorrelationId = correlationId,
            EntitlementVersion = actor?.EntitlementVersion,
            SessionId = actor?.SessionId,
            DeviceId = actor?.DeviceId,
            PolicyVersion = _policyVersion,
        };
    }
}
