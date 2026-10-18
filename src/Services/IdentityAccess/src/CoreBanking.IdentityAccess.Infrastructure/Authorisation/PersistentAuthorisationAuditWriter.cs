using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.IdentityAccess.Application.Authorisation;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;

namespace CoreBanking.IdentityAccess.Infrastructure.Authorisation;

/// <summary>Persists every permission decision before the protected request proceeds.</summary>
public sealed class PersistentAuthorisationAuditWriter : IAuthorisationAuditWriter
{
    private readonly IdentityAccessDbContext _context;

    public PersistentAuthorisationAuditWriter(IdentityAccessDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task WriteAsync(AuthorisationAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _context.AuthorisationAuditEvents.Add(new AuthorisationAuditRecord
        {
            AuditEventId = entry.AuditEventId,
            OccurredAt = entry.OccurredAt,
            ActorId = entry.ActorId?.Value,
            ActorType = entry.ActorType?.ToString(),
            InstitutionId = entry.InstitutionId?.Value,
            Permission = entry.Permission,
            Action = entry.Action,
            Decision = entry.Decision,
            Reason = entry.Reason,
            Source = entry.Source,
            CorrelationId = entry.CorrelationId,
            EntitlementVersion = entry.EntitlementVersion,
            SessionId = entry.SessionId,
            DeviceId = entry.DeviceId,
            PolicyVersion = entry.PolicyVersion,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
