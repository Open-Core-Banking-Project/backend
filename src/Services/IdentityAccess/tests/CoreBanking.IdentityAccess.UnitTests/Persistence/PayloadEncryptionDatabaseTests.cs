using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Application.Security;
using CoreBanking.IdentityAccess.Domain.Security;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Persistence;

/// <summary>
/// The one authentication-field envelope flow against a real <c>identity_access_db</c>: discovery advertises active
/// versions only, a correctly bound envelope round-trips through the boundary, and each refusal path returns the
/// code the epic names. Decisions: P1-E02-D12, P1-E02-D13.
/// </summary>
[Trait("Category", "Database")]
public sealed class PayloadEncryptionDatabaseTests : IDisposable
{
    private const string ConnectionStringVariableName = "ConnectionStrings__IdentityAccess";
    private const string KeyId = "pe-test";
    private const string Plaintext = "123456";

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("crypto-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("cryptoother-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 10, 31, 18, 0, 0, TimeSpan.Zero));
    private readonly RSA _wrappingKey;
    private readonly string _providerReference = "test-wrapping-" + Guid.NewGuid().ToString("N");

    public PayloadEncryptionDatabaseTests()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringVariableName} to the application principal connection string, or run scripts/db-verify.ps1.");
        }

        _connectionString = configured;
        _wrappingKey = RSA.Create(2048);
    }

    public void Dispose()
    {
        Discard(_institution);
        Discard(_otherInstitution);
        _wrappingKey.Dispose();
    }

    [Fact]
    public async Task Discovery_advertises_only_active_versions_of_the_requested_institution()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        PublicKeyMetadataService keys = CreateKeyService(context);

        PublicKeyView first = await keys.RegisterVersionAsync(
            _institution, KeyId, KeyPurposes.PayloadWrapping, _providerReference, CancellationToken.None);
        PublicKeyView second = await keys.RegisterVersionAsync(
            _institution, KeyId, KeyPurposes.PayloadWrapping, _providerReference, CancellationToken.None);

        Assert.Equal(1, first.KeyVersion);
        Assert.Equal(2, second.KeyVersion);
        Assert.StartsWith("-----BEGIN PUBLIC KEY-----", second.PublicKey, StringComparison.Ordinal);

        PublicKeySetView advertised = await keys.GetActiveKeysAsync(
            _institution, KeyPurposes.PayloadWrapping, CancellationToken.None);
        Assert.Equal([2, 1], advertised.Keys.Select(key => key.KeyVersion).ToArray());

        await keys.RetireAsync(_institution, KeyId, 1, CancellationToken.None);
        await keys.RevokeAsync(_institution, KeyId, 2, "compromised", CancellationToken.None);

        PublicKeySetView afterRotation = await keys.GetActiveKeysAsync(
            _institution, KeyPurposes.PayloadWrapping, CancellationToken.None);
        Assert.Empty(afterRotation.Keys);

        await using IdentityAccessDbContext otherContext = CreateContext(_otherInstitution);
        PublicKeySetView other = await CreateKeyService(otherContext).GetActiveKeysAsync(
            _otherInstitution, KeyPurposes.PayloadWrapping, CancellationToken.None);
        Assert.Empty(other.Keys);
    }

    [Fact]
    public async Task A_protected_factor_response_round_trips_and_is_audited()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        PublicKeyMetadataService keys = CreateKeyService(context);
        PublicKeyView key = await keys.RegisterVersionAsync(
            _institution, KeyId, KeyPurposes.PayloadWrapping, _providerReference, CancellationToken.None);
        PayloadEnvelopeService envelopes = CreateEnvelopeService(context);

        ProtectedPayloadEnvelope envelope = Protect(key, Plaintext, PayloadFieldBindings.StepUpResponse);
        byte[] plaintext = await envelopes.UnprotectAsync(
            Request(envelope), PayloadFieldBindings.StepUpResponse, CancellationToken.None);

        Assert.Equal(Plaintext, Encoding.UTF8.GetString(plaintext));

        List<string> events = await context.AuthenticationEvents
            .Select(evidence => evidence.EventType)
            .ToListAsync();
        Assert.Contains("payload_envelope_accepted", events);
        Assert.DoesNotContain(events, eventType => eventType == "payload_envelope_rejected");
    }

    [Fact]
    public async Task A_replayed_envelope_is_refused_generically()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        PublicKeyView key = await CreateKeyService(context).RegisterVersionAsync(
            _institution, KeyId, KeyPurposes.PayloadWrapping, _providerReference, CancellationToken.None);
        PayloadEnvelopeService envelopes = CreateEnvelopeService(context);

        ProtectedPayloadEnvelope envelope = Protect(key, Plaintext, PayloadFieldBindings.StepUpResponse);
        await envelopes.UnprotectAsync(Request(envelope), PayloadFieldBindings.StepUpResponse, CancellationToken.None);

        PayloadEnvelopeException replay = await Assert.ThrowsAsync<PayloadEnvelopeException>(() =>
            envelopes.UnprotectAsync(Request(envelope), PayloadFieldBindings.StepUpResponse, CancellationToken.None));
        Assert.Equal(PayloadEnvelopeCodes.Invalid, replay.Code);
        Assert.DoesNotContain(Plaintext, replay.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_revoked_or_expired_key_version_is_refused_with_its_own_code()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        PublicKeyMetadataService keys = CreateKeyService(context);
        PublicKeyView key = await keys.RegisterVersionAsync(
            _institution, KeyId, KeyPurposes.PayloadWrapping, _providerReference, CancellationToken.None);
        PayloadEnvelopeService envelopes = CreateEnvelopeService(context);

        ProtectedPayloadEnvelope envelope = Protect(key, Plaintext, PayloadFieldBindings.StepUpResponse);
        await keys.RevokeAsync(_institution, KeyId, key.KeyVersion, "lost device", CancellationToken.None);

        PayloadEnvelopeException revoked = await Assert.ThrowsAsync<PayloadEnvelopeException>(() =>
            envelopes.UnprotectAsync(Request(envelope), PayloadFieldBindings.StepUpResponse, CancellationToken.None));
        Assert.Equal(PayloadEnvelopeCodes.KeyRevoked, revoked.Code);

        await using IdentityAccessDbContext secondContext = CreateContext(_otherInstitution);
        PublicKeyMetadataService secondKeys = CreateKeyService(secondContext);
        PublicKeyView retiring = await secondKeys.RegisterVersionAsync(
            _otherInstitution, KeyId, KeyPurposes.PayloadWrapping, _providerReference, CancellationToken.None);
        await secondKeys.RetireAsync(_otherInstitution, KeyId, retiring.KeyVersion, CancellationToken.None);
        _time.Advance(PayloadEnvelopePolicy.RetirementOverlap + TimeSpan.FromMinutes(1));

        PayloadEnvelopeService secondEnvelopes = new(
            secondContext,
            new DevelopmentKeyOperationPort(new MapSecretProvider(_providerReference, _wrappingKey), _time),
            _time);

        ProtectedPayloadEnvelope fresh = Protect(retiring, Plaintext, PayloadFieldBindings.StepUpResponse);
        PayloadEnvelopeException expired = await Assert.ThrowsAsync<PayloadEnvelopeException>(() =>
            secondEnvelopes.UnprotectAsync(
                new PayloadEnvelopeRequest(_otherInstitution, "actor-1", "session-1", "correlation-1", fresh),
                PayloadFieldBindings.StepUpResponse,
                CancellationToken.None));
        Assert.Equal(PayloadEnvelopeCodes.KeyExpired, expired.Code);
    }

    [Fact]
    public async Task An_envelope_bound_to_another_context_or_tenant_is_refused()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        PublicKeyView key = await CreateKeyService(context).RegisterVersionAsync(
            _institution, KeyId, KeyPurposes.PayloadWrapping, _providerReference, CancellationToken.None);
        PayloadEnvelopeService envelopes = CreateEnvelopeService(context);

        ProtectedPayloadEnvelope wrongField = Protect(key, Plaintext, PayloadFieldBindings.StepUpResponse, field: "pin");
        PayloadEnvelopeException fieldMismatch = await Assert.ThrowsAsync<PayloadEnvelopeException>(() =>
            envelopes.UnprotectAsync(Request(wrongField), PayloadFieldBindings.StepUpResponse, CancellationToken.None));
        Assert.Equal(PayloadEnvelopeCodes.Invalid, fieldMismatch.Code);

        ProtectedPayloadEnvelope foreign = Protect(key, Plaintext, PayloadFieldBindings.StepUpResponse);
        await using IdentityAccessDbContext otherContext = CreateContext(_otherInstitution);
        PayloadEnvelopeException otherTenant = await Assert.ThrowsAsync<PayloadEnvelopeException>(() =>
            CreateEnvelopeService(otherContext).UnprotectAsync(
                new PayloadEnvelopeRequest(_otherInstitution, "actor-1", "session-1", "correlation-1", foreign),
                PayloadFieldBindings.StepUpResponse,
                CancellationToken.None));
        Assert.Equal(PayloadEnvelopeCodes.Invalid, otherTenant.Code);

        ProtectedPayloadEnvelope stale = Protect(
            key,
            Plaintext,
            PayloadFieldBindings.StepUpResponse,
            issuedAt: _time.GetUtcNow() - PayloadEnvelopePolicy.ClockSkew - TimeSpan.FromSeconds(1));
        PayloadEnvelopeException tooOld = await Assert.ThrowsAsync<PayloadEnvelopeException>(() =>
            envelopes.UnprotectAsync(Request(stale), PayloadFieldBindings.StepUpResponse, CancellationToken.None));
        Assert.Equal(PayloadEnvelopeCodes.Invalid, tooOld.Code);
    }

    [Fact]
    public async Task An_unconfigured_boundary_refuses_the_operation_and_never_returns_plaintext()
    {
        await using IdentityAccessDbContext context = CreateContext(_institution);
        PublicKeyView key = await CreateKeyService(context).RegisterVersionAsync(
            _institution, KeyId, KeyPurposes.PayloadWrapping, _providerReference, CancellationToken.None);

        PayloadEnvelopeService envelopes = new(context, new UnconfiguredKeyOperationPort(), _time);
        ProtectedPayloadEnvelope envelope = Protect(key, Plaintext, PayloadFieldBindings.StepUpResponse);

        PayloadEnvelopeException unavailable = await Assert.ThrowsAsync<PayloadEnvelopeException>(() =>
            envelopes.UnprotectAsync(Request(envelope), PayloadFieldBindings.StepUpResponse, CancellationToken.None));

        Assert.Equal(PayloadEnvelopeCodes.DecryptionUnavailable, unavailable.Code);
    }

    private PayloadEnvelopeRequest Request(ProtectedPayloadEnvelope envelope) =>
        new(_institution, "actor-1", "session-1", "correlation-1", envelope);

    private PayloadEnvelopeService CreateEnvelopeService(IdentityAccessDbContext context) =>
        new(context, new DevelopmentKeyOperationPort(new MapSecretProvider(_providerReference, _wrappingKey), _time), _time);

    private PublicKeyMetadataService CreateKeyService(IdentityAccessDbContext context) =>
        new(context, new DevelopmentKeyOperationPort(new MapSecretProvider(_providerReference, _wrappingKey), _time), _time);

    private ProtectedPayloadEnvelope Protect(
        PublicKeyView key,
        string plaintext,
        PayloadFieldBinding binding,
        string? field = null,
        DateTimeOffset? issuedAt = null)
    {
        DateTimeOffset issued = issuedAt ?? _time.GetUtcNow();
        byte[] contentKey = RandomNumberGenerator.GetBytes(PayloadEnvelopePolicy.ContentKeyBytes);
        byte[] nonce = RandomNumberGenerator.GetBytes(PayloadEnvelopePolicy.NonceBytes);
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        ProtectedPayloadEnvelope envelope = new(
            PayloadEnvelopePolicy.SchemaVersion,
            key.KeyId,
            key.KeyVersion,
            PayloadEnvelopePolicy.Algorithm,
            binding.Purpose,
            binding.Audience,
            field ?? binding.Field,
            issued,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            string.Empty,
            Convert.ToBase64String(nonce).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            string.Empty,
            string.Empty);

        byte[] aad = Encoding.UTF8.GetBytes(PayloadEnvelopePolicy.AdditionalData(envelope));

        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[PayloadEnvelopePolicy.TagBytes];
        using (AesGcm aes = new(contentKey, PayloadEnvelopePolicy.TagBytes))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, aad);
        }

        using RSA publicKey = RSA.Create();
        publicKey.ImportFromPem(key.PublicKey);

        return envelope with
        {
            WrappedKey = Base64Url(publicKey.Encrypt(contentKey, RSAEncryptionPadding.OaepSHA256)),
            Ciphertext = Base64Url(ciphertext),
            Tag = Base64Url(tag),
        };
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private IdentityAccessDbContext CreateContext(InstitutionId institution)
    {
        AmbientTenantContext tenant = new();
        tenant.Establish(institution);

        return new IdentityAccessDbContext(
            new DbContextOptionsBuilder<IdentityAccessDbContext>().UseNpgsql(_connectionString).Options,
            tenant);
    }

    private void Discard(InstitutionId institution)
    {
        using IdentityAccessDbContext context = CreateContext(institution);

        context.AuthenticationEvents.ExecuteDelete();
        context.EnvelopeReplays.ExecuteDelete();
        context.SecurityKeyMetadata.ExecuteDelete();
    }

    /// <summary>The secret provider stand-in: one logical name maps to the test wrapping key.</summary>
    private sealed class MapSecretProvider : ISecretProvider
    {
        private readonly string _logicalName;
        private readonly RSA _key;

        public MapSecretProvider(string logicalName, RSA key)
        {
            _logicalName = logicalName;
            _key = key;
        }

        public Task<string?> TryReadAsync(string logicalName, CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(logicalName, _logicalName, StringComparison.Ordinal)
                ? _key.ExportPkcs8PrivateKeyPem()
                : null);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
