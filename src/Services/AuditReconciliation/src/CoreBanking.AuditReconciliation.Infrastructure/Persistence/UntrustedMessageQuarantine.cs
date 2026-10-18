using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.AuditReconciliation.Infrastructure.Persistence;

/// <summary>
/// A transport message that could not establish trusted institution context. This record is
/// intentionally not tenant-owned; the original coordinates make repeated delivery idempotent.
/// </summary>
public sealed class UntrustedMessageQuarantineRecord
{
    public required string Transport { get; set; }

    public required string Stream { get; set; }

    public int Partition { get; set; }

    public long Position { get; set; }

    public string? MessageKey { get; set; }

    public required string Payload { get; set; }

    public required string Reason { get; set; }

    public DateTimeOffset QuarantinedAt { get; set; }

    public DateTimeOffset? RepairedAt { get; set; }
}

/// <summary>Audit Reconciliation's durable quarantine for malformed integration messages.</summary>
public sealed class PersistentUntrustedMessageQuarantine : IUntrustedMessageQuarantine
{
    private readonly AuditReconciliationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public PersistentUntrustedMessageQuarantine(
        AuditReconciliationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task QuarantineAsync(UntrustedMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        UntrustedMessageQuarantineRecord? existing = await _context.UntrustedMessageQuarantine
            .FindAsync([message.Transport, message.Stream, message.Partition, message.Position], cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return;
        }

        _context.UntrustedMessageQuarantine.Add(new UntrustedMessageQuarantineRecord
        {
            Transport = message.Transport,
            Stream = message.Stream,
            Partition = message.Partition,
            Position = message.Position,
            MessageKey = message.MessageKey,
            Payload = message.Payload,
            Reason = message.Reason,
            QuarantinedAt = _timeProvider.GetUtcNow(),
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
