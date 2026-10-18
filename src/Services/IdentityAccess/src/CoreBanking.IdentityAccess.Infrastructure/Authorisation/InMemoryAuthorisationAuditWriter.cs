using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.IdentityAccess.Application.Authorisation;

namespace CoreBanking.IdentityAccess.Infrastructure.Authorisation;

/// <summary>
/// Development and test implementation of the authorisation evidence writer. Production uses the
/// durable database writer. This implementation exposes no update or delete operation.
/// Decision: ADR-0010.
/// </summary>
public sealed class InMemoryAuthorisationAuditWriter : IAuthorisationAuditWriter
{
    private readonly object _gate = new();
    private readonly List<AuthorisationAuditEntry> _entries = new();

    public IReadOnlyList<AuthorisationAuditEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    public Task WriteAsync(AuthorisationAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _entries.Add(entry);
        }

        return Task.CompletedTask;
    }
}
