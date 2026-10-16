using System;
using System.Collections.Generic;
using CoreBanking.IdentityAccess.Application.Authorisation;

namespace CoreBanking.IdentityAccess.Infrastructure.Authorisation;

/// <summary>
/// Development and test implementation of the authorisation evidence writer. Entries are
/// append-only through this type: it exposes no update or delete operation. The durable
/// implementation arrives with the audit foundation in epic 05.
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

    public void Write(AuthorisationAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            _entries.Add(entry);
        }
    }
}
