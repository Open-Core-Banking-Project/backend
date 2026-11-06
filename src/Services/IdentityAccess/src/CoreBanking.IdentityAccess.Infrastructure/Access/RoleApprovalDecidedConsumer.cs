using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Access;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IdentityAccess.Infrastructure.Access;

/// <summary>Applies a trusted Workflow decision only to the exact frozen role version submitted.</summary>
public sealed class RoleApprovalDecidedConsumer : IIntegrationEventConsumer
{
    public const string SourceProducer = "operations-workflow";
    public const string ConsumerName = "identity-access.role-approval-decided";
    public const int SchemaVersion = 1;

    private readonly IdentityAccessDbContext _context;
    private readonly InboxProcessor _inbox;
    private readonly AmbientTenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public RoleApprovalDecidedConsumer(
        IdentityAccessDbContext context,
        InboxProcessor inbox,
        AmbientTenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public static string TopicName => EventContract.TopicFor(
        SourceProducer,
        RolePublicationApprovalV1.EventFamily,
        SchemaVersion);

    public string Topic => TopicName;

    public async Task<InboxResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _tenantContext.Clear();
        _tenantContext.Establish(InstitutionId.From(envelope.InstitutionId));
        try
        {
            return await _inbox.ProcessAsync(
                ConsumerName,
                envelope,
                SchemaVersion,
                token => ApplyAsync(envelope, token),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _tenantContext.Clear();
        }
    }

    private async Task<string?> ApplyAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!string.Equals(envelope.Producer, SourceProducer, StringComparison.Ordinal)
            || !string.Equals(envelope.EventType, RolePublicationApprovalV1.DecidedEventType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The role approval topic contained an unexpected producer or event type.");
        }

        RoleApprovalDecidedV1 payload = EventContract.Deserialize<RoleApprovalDecidedV1>(envelope.Payload)
            ?? throw new InvalidOperationException("The role approval decision payload is missing.");
        Validate(envelope, payload);

        RoleRecord role = await _context.Roles
            .SingleOrDefaultAsync(record => record.RoleId == payload.SubjectId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The role named by the approval decision is unavailable.");

        if (string.Equals(role.WorkflowDecisionReference, payload.DecisionId, StringComparison.Ordinal))
        {
            return "already_applied:" + payload.DecisionId;
        }

        if (!string.Equals(role.ApprovalRequestId, payload.RequestId, StringComparison.Ordinal)
            || !string.Equals(role.ApprovalContentHash, payload.SubjectHash, StringComparison.Ordinal)
            || !string.Equals(role.Version.ToString(CultureInfo.InvariantCulture), payload.SubjectVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(role.SubmittedBy))
        {
            throw new InvalidOperationException("The approval decision does not match the exact frozen role version.");
        }

        if (string.Equals(role.SubmittedBy, payload.ApproverActorId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The role maker cannot apply their own approval decision.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool approved = string.Equals(payload.Decision, "approved", StringComparison.Ordinal);
        role.WorkflowDecisionReference = payload.DecisionId;
        role.Decision = payload.Decision;
        role.DecidedBy = payload.ApproverActorId;
        role.DecidedAt = payload.DecidedAt;
        role.ConcurrencyVersion += 1;
        role.UpdatedAt = now;

        if (approved)
        {
            string[] permissions = await _context.RolePermissions
                .Where(permission => permission.RoleId == role.RoleId)
                .Select(permission => permission.PermissionCode)
                .OrderBy(permission => permission)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);

            _context.RoleVersions.Add(new RoleVersionRecord
            {
                InstitutionId = payload.InstitutionId,
                RoleId = role.RoleId,
                Version = role.Version,
                RoleCode = role.RoleCode,
                RoleName = role.RoleName,
                ContentHash = payload.SubjectHash,
                ApprovalRequestId = payload.RequestId,
                WorkflowDecisionReference = payload.DecisionId,
                PublishedBy = payload.ApproverActorId,
                PublishedAt = now,
            });
            foreach (string permission in permissions)
            {
                _context.RoleVersionPermissions.Add(new RoleVersionPermissionRecord
                {
                    InstitutionId = payload.InstitutionId,
                    RoleId = role.RoleId,
                    RoleVersion = role.Version,
                    PermissionCode = permission,
                });
            }

            role.Status = RecordStatus.Active;
            role.PublishedAt = now;
            AddPublishedEvent(role, payload, now);
        }
        else
        {
            role.ApprovalRequestId = null;
            role.ApprovalContentHash = null;
            role.SubmittedBy = null;
            role.SubmittedAt = null;
        }

        AddApplicationResult(payload, approved, now);
        _context.OperatorActionAuditEvents.Add(new OperatorActionAuditRecord
        {
            InstitutionId = payload.InstitutionId,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = payload.ApproverActorId,
            Action = approved ? "role_published" : "role_publication_rejected",
            ResourceType = "role",
            ResourceId = role.RoleId,
            Reason = payload.Reason ?? payload.Decision,
            CorrelationId = payload.CorrelationId,
            OccurredAt = now,
        });

        return payload.Decision + ":" + payload.DecisionId;
    }

    private void AddPublishedEvent(RoleRecord role, RoleApprovalDecidedV1 decision, DateTimeOffset now)
    {
        AddOutbox(
            RolePublicationApprovalV1.PublishedEventType,
            "access",
            role.RoleId,
            decision.CorrelationId,
            new RoleVersionPublishedV1(
                decision.InstitutionId,
                role.RoleId,
                role.RoleCode,
                role.Version,
                decision.SubjectHash,
                decision.RequestId,
                decision.DecisionId,
                decision.ApproverActorId,
                now),
            now);
    }

    private void AddApplicationResult(RoleApprovalDecidedV1 decision, bool approved, DateTimeOffset now)
    {
        AddOutbox(
            RolePublicationApprovalV1.ApplicationResultEventType,
            RolePublicationApprovalV1.EventFamily,
            decision.RequestId,
            decision.CorrelationId,
            new RoleApprovalApplicationResultV1(
                decision.InstitutionId,
                decision.RequestId,
                decision.DecisionId,
                decision.DecisionId,
                decision.WorkflowKey,
                decision.SubjectType,
                decision.SubjectId,
                decision.SubjectVersion,
                decision.SubjectHash,
                RolePublicationApprovalV1.SourceService,
                approved ? "applied" : "rejected",
                FailureCode: null,
                decision.Reason,
                decision.CorrelationId,
                now),
            now);
    }

    private void AddOutbox(string eventType, string family, string reference, string correlationId, object payload, DateTimeOffset now)
    {
        string eventId = Guid.NewGuid().ToString("N");
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = eventType,
            SchemaVersion = SchemaVersion,
            OccurredAt = now,
            InstitutionId = _tenantContext.CurrentInstitution?.Value
                ?? throw new InvalidOperationException("A trusted institution context is required."),
            CorrelationId = correlationId,
            Producer = RolePublicationApprovalV1.SourceService,
            BusinessReference = reference,
            Payload = EventContract.Serialize(payload),
        };

        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = envelope.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = eventType,
            SchemaVersion = SchemaVersion,
            Topic = EventContract.TopicFor(RolePublicationApprovalV1.SourceService, family, SchemaVersion),
            BusinessReference = reference,
            CorrelationId = correlationId,
            Producer = RolePublicationApprovalV1.SourceService,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });
    }

    private static void Validate(IntegrationEventEnvelope envelope, RoleApprovalDecidedV1 payload)
    {
        bool decisionValid = string.Equals(payload.Decision, "approved", StringComparison.Ordinal)
            || string.Equals(payload.Decision, "rejected", StringComparison.Ordinal);
        if (!string.Equals(payload.InstitutionId, envelope.InstitutionId, StringComparison.Ordinal)
            || !string.Equals(payload.RequestId, envelope.BusinessReference, StringComparison.Ordinal)
            || !string.Equals(payload.CorrelationId, envelope.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowKey, RolePublicationApprovalV1.WorkflowKey, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowVersionId, RolePublicationApprovalV1.WorkflowVersionId, StringComparison.Ordinal)
            || !string.Equals(payload.WorkflowDefinitionHash, RolePublicationApprovalV1.DefinitionHash, StringComparison.Ordinal)
            || !string.Equals(payload.SubjectType, RolePublicationApprovalV1.SubjectType, StringComparison.Ordinal)
            || !string.Equals(payload.Permission, RolePublicationApprovalV1.RequiredPermission, StringComparison.Ordinal)
            || payload.EntitlementValidUntil <= payload.EntitlementValidFrom
            || payload.DecidedAt < payload.EntitlementValidFrom
            || payload.DecidedAt >= payload.EntitlementValidUntil
            || !decisionValid)
        {
            throw new InvalidOperationException(
                "The role approval decision does not match the trusted envelope, profile, or decision-time entitlement evidence.");
        }
    }
}
