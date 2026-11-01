using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Access;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IdentityAccess.Infrastructure.Access;

/// <summary>
/// Detects state that a healthy identity service should never hold: grants without a version, lapsed
/// grants the sweep has not closed, expired service credentials still active and step-up evidence
/// that can no longer be consumed. Detection is read-only; the repair is an explicit operator action.
/// Decision: ADR-0009, ADR-0010; slice 14.
/// </summary>
public sealed record EntitlementReconciliationFindings(
    long GrantHoldersWithoutVersion,
    long ExpiredGrantsNotClosed,
    long ExpiredServiceClientsActive,
    long StaleStepUpEvidence)
{
    public bool IsClean =>
        GrantHoldersWithoutVersion == 0
        && ExpiredGrantsNotClosed == 0
        && ExpiredServiceClientsActive == 0
        && StaleStepUpEvidence == 0;
}

public sealed class EntitlementReconciliationService
{
    private readonly IdentityAccessDbContext _context;

    public EntitlementReconciliationService(IdentityAccessDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<EntitlementReconciliationFindings> ReconcileAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The pass runs in the worker, which has no request context, so every query ignores the
        // tenant filter and compares institution keys explicitly.
        HashSet<(string InstitutionId, string IdentityId)> holders = (await _context.IdentityRoles
                .IgnoreQueryFilters()
                .Where(assignment => assignment.ValidFrom <= now && (assignment.ValidTo == null || assignment.ValidTo > now))
                .Select(assignment => new { assignment.InstitutionId, assignment.IdentityId })
                .Distinct()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(row => (row.InstitutionId, row.IdentityId))
            .ToHashSet();

        foreach (TemporaryGrantRecord grant in await _context.TemporaryGrants
                     .IgnoreQueryFilters()
                     .Where(grant => grant.ExpiredAt == null && grant.ValidFrom <= now && grant.ValidTo > now)
                     .ToArrayAsync(cancellationToken)
                     .ConfigureAwait(false))
        {
            holders.Add((grant.InstitutionId, grant.IdentityId));
        }

        HashSet<(string InstitutionId, string IdentityId)> versioned = (await _context.EntitlementVersions
                .IgnoreQueryFilters()
                .Where(version => version.Version > 0)
                .Select(version => new { version.InstitutionId, version.IdentityId })
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(row => (row.InstitutionId, row.IdentityId))
            .ToHashSet();

        long grantHoldersWithoutVersion = holders.Count(holder => !versioned.Contains(holder));

        long expiredGrantsNotClosed = await _context.TemporaryGrants
            .IgnoreQueryFilters()
            .LongCountAsync(grant => grant.ExpiredAt == null && grant.ValidTo <= now, cancellationToken)
            .ConfigureAwait(false);

        long expiredServiceClientsActive = await _context.ServiceClients
            .IgnoreQueryFilters()
            .LongCountAsync(
                client => client.Status == RecordStatus.Active
                    && client.CredentialExpiresAt != null
                    && client.CredentialExpiresAt <= now,
                cancellationToken)
            .ConfigureAwait(false);

        long staleStepUpEvidence = await _context.StepUpEvidence
            .IgnoreQueryFilters()
            .LongCountAsync(evidence => evidence.ConsumedAt == null && evidence.ExpiresAt <= now, cancellationToken)
            .ConfigureAwait(false);

        return new EntitlementReconciliationFindings(
            grantHoldersWithoutVersion,
            expiredGrantsNotClosed,
            expiredServiceClientsActive,
            staleStepUpEvidence);
    }
}
