using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Application.Security;
using CoreBanking.IdentityAccess.Domain.Security;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.IdentityAccess.Infrastructure.Security;

/// <summary>
/// Institution key metadata. Discovery answers with public parameters of active versions only, and rotation moves a
/// version to retired and then revoked. No key material is created, exported or stored here: the boundary already
/// holds it and this service records what the boundary reports.
/// Decision: P1-E02-D13 items D13-C, D13-F and D13-H.
/// </summary>
public sealed class PublicKeyMetadataService : IPublicKeyMetadataService
{
    private readonly IdentityAccessDbContext _context;
    private readonly IKeyOperationPort _keyOperations;
    private readonly TimeProvider _timeProvider;

    public PublicKeyMetadataService(
        IdentityAccessDbContext context,
        IKeyOperationPort keyOperations,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _keyOperations = keyOperations ?? throw new ArgumentNullException(nameof(keyOperations));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<PublicKeySetView> GetActiveKeysAsync(
        InstitutionId institutionId,
        string purpose,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<SecurityKeyMetadataRecord> records = await _context.SecurityKeyMetadata
            .AsNoTracking()
            .Where(key => key.InstitutionId == institutionId.Value && key.Purpose == purpose)
            .OrderByDescending(key => key.KeyVersion)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<PublicKeyView> keys = records
            .Select(record => ToMetadata(record))
            .Where(key => key.IsAdvertisedAt(now))
            .Select(ToView)
            .ToList();

        return new PublicKeySetView(purpose, keys);
    }

    public async Task<PublicKeyView> RegisterVersionAsync(
        InstitutionId institutionId,
        string keyId,
        string purpose,
        string providerReference,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerReference);

        KeyDescription description = await _keyOperations
            .DescribeAsync(providerReference, cancellationToken)
            .ConfigureAwait(false);

        List<int> versions = await _context.SecurityKeyMetadata
            .Where(key => key.InstitutionId == institutionId.Value && key.KeyId == keyId.Trim())
            .Select(key => key.KeyVersion)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        SecurityKeyMetadata metadata = SecurityKeyMetadata.Register(
            institutionId.Value,
            keyId,
            versions.Count == 0 ? 1 : versions.Max() + 1,
            purpose,
            description.Algorithm,
            description.PublicKey,
            providerReference,
            now);

        _context.SecurityKeyMetadata.Add(ToRecord(metadata));
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToView(metadata);
    }

    public async Task<PublicKeyView> RetireAsync(
        InstitutionId institutionId,
        string keyId,
        int keyVersion,
        CancellationToken cancellationToken)
    {
        (SecurityKeyMetadataRecord record, SecurityKeyMetadata metadata) =
            await LoadAsync(institutionId, keyId, keyVersion, cancellationToken).ConfigureAwait(false);

        metadata.Retire(_timeProvider.GetUtcNow());
        Apply(metadata, record);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToView(metadata);
    }

    public async Task<PublicKeyView> RevokeAsync(
        InstitutionId institutionId,
        string keyId,
        int keyVersion,
        string reason,
        CancellationToken cancellationToken)
    {
        (SecurityKeyMetadataRecord record, SecurityKeyMetadata metadata) =
            await LoadAsync(institutionId, keyId, keyVersion, cancellationToken).ConfigureAwait(false);

        metadata.Revoke(reason, _timeProvider.GetUtcNow());
        Apply(metadata, record);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToView(metadata);
    }

    private async Task<(SecurityKeyMetadataRecord Record, SecurityKeyMetadata Metadata)> LoadAsync(
        InstitutionId institutionId,
        string keyId,
        int keyVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        SecurityKeyMetadataRecord record = await _context.SecurityKeyMetadata
            .SingleOrDefaultAsync(
                key => key.InstitutionId == institutionId.Value && key.KeyId == keyId.Trim() && key.KeyVersion == keyVersion,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The key version was not found.");

        return (record, ToMetadata(record));
    }

    private static void Apply(SecurityKeyMetadata metadata, SecurityKeyMetadataRecord record)
    {
        record.State = metadata.State;
        record.RetiredAt = metadata.RetiredAt;
        record.RevokedAt = metadata.RevokedAt;
        record.RevocationReason = metadata.RevocationReason;
    }

    private static PublicKeyView ToView(SecurityKeyMetadata metadata) =>
        new(
            metadata.KeyId,
            metadata.Version,
            metadata.Purpose,
            metadata.Algorithm,
            metadata.PublicKey,
            metadata.NotBefore,
            metadata.NotAfter);

    private static SecurityKeyMetadata ToMetadata(SecurityKeyMetadataRecord record) =>
        SecurityKeyMetadata.Restore(
            record.InstitutionId,
            record.KeyId,
            record.KeyVersion,
            record.Purpose,
            record.Algorithm,
            record.PublicKey,
            record.ProviderReference,
            record.State,
            record.CreatedAt,
            record.NotBefore,
            record.NotAfter,
            record.RetiredAt,
            record.RevokedAt,
            record.RevocationReason);

    private static SecurityKeyMetadataRecord ToRecord(SecurityKeyMetadata metadata) => new()
    {
        InstitutionId = metadata.InstitutionId,
        KeyId = metadata.KeyId,
        KeyVersion = metadata.Version,
        Purpose = metadata.Purpose,
        Algorithm = metadata.Algorithm,
        PublicKey = metadata.PublicKey,
        ProviderReference = metadata.ProviderReference,
        State = metadata.State,
        CreatedAt = metadata.CreatedAt,
        NotBefore = metadata.NotBefore,
        NotAfter = metadata.NotAfter,
        RetiredAt = metadata.RetiredAt,
        RevokedAt = metadata.RevokedAt,
        RevocationReason = metadata.RevocationReason,
    };
}
