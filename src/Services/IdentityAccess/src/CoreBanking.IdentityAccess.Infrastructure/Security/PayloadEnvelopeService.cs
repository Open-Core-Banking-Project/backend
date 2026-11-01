using System;
using System.Buffers.Text;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.IdentityAccess.Application.Security;
using CoreBanking.IdentityAccess.Domain.Security;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoreBanking.IdentityAccess.Infrastructure.Security;

/// <summary>
/// The one authentication-field envelope flow of Phase 1 (IAM-UC-07). The order is deliberate: structural
/// validation, then key usability, then the replay claim, then the boundary unwrap, then authenticated decryption.
/// Every refusal is one of the four codes the epic names and never discloses cryptographic detail.
/// Decisions: P1-E02-D12, P1-E02-D13.
/// </summary>
public sealed class PayloadEnvelopeService : IPayloadEnvelopeService
{
    private const string AcceptedEventType = "payload_envelope_accepted";
    private const string RejectedEventType = "payload_envelope_rejected";
    private const int MinimumReplayIdChars = 16;
    private const int MaximumReplayIdChars = 64;

    private readonly IdentityAccessDbContext _context;
    private readonly IKeyOperationPort _keyOperations;
    private readonly TimeProvider _timeProvider;

    public PayloadEnvelopeService(
        IdentityAccessDbContext context,
        IKeyOperationPort keyOperations,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _keyOperations = keyOperations ?? throw new ArgumentNullException(nameof(keyOperations));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<byte[]> UnprotectAsync(
        PayloadEnvelopeRequest request,
        PayloadFieldBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(binding);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        try
        {
            (byte[] wrappedKey, byte[] nonce, byte[] ciphertext, byte[] tag) = Validate(request.Envelope, binding, now);

            SecurityKeyMetadataRecord record = await LoadKeyAsync(request, cancellationToken).ConfigureAwait(false);
            SecurityKeyMetadata key = ToMetadata(record);
            switch (key.Evaluate(now, PayloadEnvelopePolicy.RetirementOverlap))
            {
                case KeyUsability.Revoked:
                    throw new PayloadEnvelopeException(PayloadEnvelopeCodes.KeyRevoked, "The envelope key version is revoked.");
                case KeyUsability.NotYetValid:
                case KeyUsability.Expired:
                case KeyUsability.RetiredBeyondOverlap:
                    throw new PayloadEnvelopeException(PayloadEnvelopeCodes.KeyExpired, "The envelope key version is not valid now.");
                default:
                    break;
            }

            await ClaimReplayAsync(request, now, cancellationToken).ConfigureAwait(false);

            byte[] contentKey = await UnwrapAsync(request, record, wrappedKey, cancellationToken).ConfigureAwait(false);
            try
            {
                byte[] plaintext = new byte[ciphertext.Length];
                try
                {
                    using AesGcm aes = new(contentKey, PayloadEnvelopePolicy.TagBytes);
                    aes.Decrypt(
                        nonce,
                        ciphertext,
                        tag,
                        plaintext,
                        Encoding.UTF8.GetBytes(PayloadEnvelopePolicy.AdditionalData(request.Envelope)));
                }
                catch (CryptographicException exception)
                {
                    throw new PayloadEnvelopeException(
                        PayloadEnvelopeCodes.Invalid,
                        "The envelope could not be authenticated.",
                        exception);
                }

                await AuditAsync(request, AcceptedEventType, AuthenticationOutcome.Succeeded, "envelope accepted", now, cancellationToken)
                    .ConfigureAwait(false);

                return plaintext;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(contentKey);
            }
        }
        catch (PayloadEnvelopeException exception)
        {
            await AuditAsync(request, RejectedEventType, AuthenticationOutcome.Failed, exception.Code, now, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Structural and contextual validation. Everything that can be refused without the boundary is refused here,
    /// with one indistinguishable code.
    /// </summary>
    private static (byte[] WrappedKey, byte[] Nonce, byte[] Ciphertext, byte[] Tag) Validate(
        ProtectedPayloadEnvelope envelope,
        PayloadFieldBinding binding,
        DateTimeOffset now)
    {
        if (envelope is null)
        {
            throw Invalid();
        }

        if (envelope.SchemaVersion != PayloadEnvelopePolicy.SchemaVersion
            || !string.Equals(envelope.Algorithm, PayloadEnvelopePolicy.Algorithm, StringComparison.Ordinal)
            || !string.Equals(envelope.Purpose, binding.Purpose, StringComparison.Ordinal)
            || !string.Equals(envelope.Audience, binding.Audience, StringComparison.Ordinal)
            || !string.Equals(envelope.Field, binding.Field, StringComparison.Ordinal))
        {
            throw Invalid();
        }

        if (string.IsNullOrWhiteSpace(envelope.KeyId)
            || envelope.KeyId.Length > SecurityKeyMetadata.MaximumKeyIdLength
            || envelope.KeyVersion < 1
            || !ValidReplayId(envelope.ReplayId))
        {
            throw Invalid();
        }

        if ((now - envelope.IssuedAt).Duration() > PayloadEnvelopePolicy.ClockSkew)
        {
            throw Invalid();
        }

        byte[] wrappedKey = Decode(envelope.WrappedKey, PayloadEnvelopePolicy.MaximumWrappedKeyBytes);
        byte[] nonce = Decode(envelope.Nonce, PayloadEnvelopePolicy.NonceBytes, exact: true);
        byte[] ciphertext = Decode(envelope.Ciphertext, PayloadEnvelopePolicy.MaximumCiphertextBytes);
        byte[] tag = Decode(envelope.Tag, PayloadEnvelopePolicy.TagBytes, exact: true);

        if (wrappedKey.Length == 0 || ciphertext.Length == 0)
        {
            throw Invalid();
        }

        return (wrappedKey, nonce, ciphertext, tag);
    }

    private async Task<SecurityKeyMetadataRecord> LoadKeyAsync(
        PayloadEnvelopeRequest request,
        CancellationToken cancellationToken)
    {
        SecurityKeyMetadataRecord? record = await _context.SecurityKeyMetadata
            .AsNoTracking()
            .SingleOrDefaultAsync(
                key => key.InstitutionId == request.InstitutionId.Value
                    && key.KeyId == request.Envelope.KeyId
                    && key.KeyVersion == request.Envelope.KeyVersion,
                cancellationToken)
            .ConfigureAwait(false);

        // An unknown key and a ciphertext bound to another tenant are the same answer to a caller.
        return record ?? throw Invalid();
    }

    /// <summary>
    /// Claims the replay identifier. The unique key on (institution, purpose, replayId) is the control; a duplicate
    /// insert is caught and reported generically, so a replay cannot be distinguished from a malformed envelope.
    /// </summary>
    private async Task ClaimReplayAsync(
        PayloadEnvelopeRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await _context.EnvelopeReplays
            .Where(replay => replay.InstitutionId == request.InstitutionId.Value && replay.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // A replay is already claimed when the same identifier is held by the database or by this unit of work.
        bool alreadyClaimed = _context.ChangeTracker
            .Entries<EnvelopeReplayRecord>()
            .Any(entry => string.Equals(entry.Entity.InstitutionId, request.InstitutionId.Value, StringComparison.Ordinal)
                && string.Equals(entry.Entity.Purpose, request.Envelope.Purpose, StringComparison.Ordinal)
                && string.Equals(entry.Entity.ReplayId, request.Envelope.ReplayId, StringComparison.Ordinal));

        if (alreadyClaimed)
        {
            _context.ChangeTracker.Clear();
            throw Invalid();
        }

        _context.EnvelopeReplays.Add(new EnvelopeReplayRecord
        {
            InstitutionId = request.InstitutionId.Value,
            Purpose = request.Envelope.Purpose,
            ReplayId = request.Envelope.ReplayId,
            ClaimedAt = now,
            ExpiresAt = now.Add(PayloadEnvelopePolicy.ReplayWindow),
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            _context.ChangeTracker.Clear();
            throw Invalid();
        }
    }

    private async Task<byte[]> UnwrapAsync(
        PayloadEnvelopeRequest request,
        SecurityKeyMetadataRecord record,
        byte[] wrappedKey,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] contentKey = await _keyOperations.UnwrapAsync(
                new KeyOperationRequest(record.ProviderReference, record.KeyVersion, request.Envelope.Algorithm, wrappedKey),
                cancellationToken).ConfigureAwait(false);

            if (contentKey is null || contentKey.Length != PayloadEnvelopePolicy.ContentKeyBytes)
            {
                if (contentKey is not null)
                {
                    CryptographicOperations.ZeroMemory(contentKey);
                }

                throw new KeyOperationException("The boundary returned a content key of an unexpected length.");
            }

            return contentKey;
        }
        catch (KeyOperationException exception)
        {
            throw new PayloadEnvelopeException(
                PayloadEnvelopeCodes.DecryptionUnavailable,
                "Envelope decryption is temporarily unavailable.",
                exception);
        }
    }

    private async Task AuditAsync(
        PayloadEnvelopeRequest request,
        string eventType,
        AuthenticationOutcome outcome,
        string detail,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        _context.AuthenticationEvents.Add(new AuthenticationEventRecord
        {
            InstitutionId = request.InstitutionId.Value,
            AuthenticationEventId = Guid.NewGuid().ToString("N"),
            IdentityId = request.IdentityId,
            EventType = eventType,
            Outcome = outcome,
            Detail = $"{detail}; key={request.Envelope?.KeyId}#{request.Envelope?.KeyVersion}; correlation={request.CorrelationId}",
            OccurredAt = now,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static bool ValidReplayId(string replayId) =>
        !string.IsNullOrWhiteSpace(replayId)
        && replayId.Length is >= MinimumReplayIdChars and <= MaximumReplayIdChars
        && replayId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static byte[] Decode(string value, int maximumBytes, bool exact = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid();
        }

        byte[] decoded;
        try
        {
            decoded = Base64Url.DecodeFromChars(value);
        }
        catch (FormatException)
        {
            throw Invalid();
        }

        if (exact ? decoded.Length != maximumBytes : decoded.Length > maximumBytes)
        {
            throw Invalid();
        }

        return decoded;
    }

    private static PayloadEnvelopeException Invalid() =>
        new(PayloadEnvelopeCodes.Invalid, "The protected payload envelope is not acceptable.");

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
}
