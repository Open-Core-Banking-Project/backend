using System;
using CoreBanking.IdentityAccess.Application.Security;
using CoreBanking.IdentityAccess.Domain.Security;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Domain;

/// <summary>
/// Envelope rules and key custody rules without a database. The authenticated context must bind purpose, field,
/// key version and issue time, and a key must stop unwrapping at exactly the approved boundaries.
/// Decisions: P1-E02-D12, P1-E02-D13.
/// </summary>
public sealed class PayloadEnvelopeRulesTests
{
    private const string PublicKey = "-----BEGIN PUBLIC KEY-----\nMIIB\n-----END PUBLIC KEY-----";

    private static readonly DateTimeOffset Now = new(2026, 10, 31, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_authenticated_context_binds_purpose_audience_field_key_and_issue_time()
    {
        ProtectedPayloadEnvelope envelope = Envelope(purpose: "identity.step_up.response", field: "response", keyVersion: 2);

        string additionalData = PayloadEnvelopePolicy.AdditionalData(envelope);

        Assert.Equal("v1|identity.step_up.response|identity-access|response|pe-1|2|2026-10-31T18:00:00Z", additionalData);
        Assert.NotEqual(additionalData, PayloadEnvelopePolicy.AdditionalData(envelope with { Field = "pin" }));
        Assert.NotEqual(additionalData, PayloadEnvelopePolicy.AdditionalData(envelope with { KeyVersion = 3 }));
        Assert.NotEqual(additionalData, PayloadEnvelopePolicy.AdditionalData(envelope with { Purpose = "identity.pin.set" }));
    }

    [Fact]
    public void The_issue_time_is_truncated_to_whole_seconds_so_client_precision_cannot_change_the_context()
    {
        ProtectedPayloadEnvelope envelope = Envelope() with
        {
            IssuedAt = new DateTimeOffset(2026, 10, 31, 18, 0, 0, 987, TimeSpan.Zero),
        };

        Assert.Equal("2026-10-31T18:00:00Z", PayloadEnvelopePolicy.CanonicalIssuedAt(envelope.IssuedAt));
    }

    [Fact]
    public void Only_the_approved_algorithm_suite_is_accepted()
    {
        Assert.Equal(PayloadEnvelopePolicy.Algorithm, SecurityKeyMetadata.ApprovedAlgorithms[0]);
        Assert.Single(SecurityKeyMetadata.ApprovedAlgorithms);

        Assert.Throws<ArgumentException>(() => SecurityKeyMetadata.Register(
            "institution-1", "pe-1", 1, KeyPurposes.PayloadWrapping, "RSA-PKCS1+A128CBC", PublicKey, "ref", Now));
    }

    [Fact]
    public void A_key_version_is_usable_only_inside_its_window_and_overlap()
    {
        SecurityKeyMetadata key = Registered(notAfter: Now.AddDays(30));

        Assert.Equal(KeyUsability.Usable, key.Evaluate(Now, PayloadEnvelopePolicy.RetirementOverlap));
        Assert.Equal(KeyUsability.NotYetValid, key.Evaluate(Now.AddMinutes(-1), PayloadEnvelopePolicy.RetirementOverlap));
        Assert.Equal(KeyUsability.Expired, key.Evaluate(Now.AddDays(31), PayloadEnvelopePolicy.RetirementOverlap));

        key.Retire(Now);
        Assert.False(key.IsAdvertisedAt(Now));
        Assert.Equal(KeyUsability.Usable, key.Evaluate(Now.AddHours(23), PayloadEnvelopePolicy.RetirementOverlap));
        Assert.Equal(
            KeyUsability.RetiredBeyondOverlap,
            key.Evaluate(Now.AddHours(25), PayloadEnvelopePolicy.RetirementOverlap));
    }

    [Fact]
    public void A_revoked_key_version_never_unwraps_again()
    {
        SecurityKeyMetadata key = Registered();

        key.Revoke("suspected compromise", Now);

        Assert.Equal(KeyUsability.Revoked, key.Evaluate(Now, PayloadEnvelopePolicy.RetirementOverlap));
        Assert.Equal(KeyCustodyState.Revoked, key.State);
        Assert.Equal("suspected compromise", key.RevocationReason);
        Assert.Throws<InvalidOperationException>(() => key.Retire(Now));
    }

    [Fact]
    public void Key_registration_refuses_an_invalid_version_identifier_or_window()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SecurityKeyMetadata.Register(
            "institution-1", "pe-1", 0, KeyPurposes.PayloadWrapping, PayloadEnvelopePolicy.Algorithm, PublicKey, "ref", Now));
        Assert.Throws<ArgumentException>(() => SecurityKeyMetadata.Register(
            "institution-1", new string('k', 65), 1, KeyPurposes.PayloadWrapping, PayloadEnvelopePolicy.Algorithm, PublicKey, "ref", Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => SecurityKeyMetadata.Register(
            "institution-1", "pe-1", 1, KeyPurposes.PayloadWrapping, PayloadEnvelopePolicy.Algorithm, PublicKey, "ref", Now, Now));
    }

    [Fact]
    public void Revocation_requires_a_reason()
    {
        SecurityKeyMetadata key = Registered();

        Assert.Throws<ArgumentException>(() => key.Revoke("   ", Now));
        Assert.Equal(KeyCustodyState.Active, key.State);
    }

    private static SecurityKeyMetadata Registered(DateTimeOffset? notAfter = null) =>
        SecurityKeyMetadata.Register(
            "institution-1",
            "pe-1",
            1,
            KeyPurposes.PayloadWrapping,
            PayloadEnvelopePolicy.Algorithm,
            PublicKey,
            "kms://institution-1/payload-wrapping/1",
            Now,
            notAfter);

    private static ProtectedPayloadEnvelope Envelope(
        string purpose = "identity.step_up.response",
        string field = "response",
        int keyVersion = 2) =>
        new(
            PayloadEnvelopePolicy.SchemaVersion,
            "pe-1",
            keyVersion,
            PayloadEnvelopePolicy.Algorithm,
            purpose,
            "identity-access",
            field,
            Now,
            "0123456789abcdef0123456789abcdef",
            "d3JhcHBlZA",
            "bm9uY2U",
            "Y2lwaGVy",
            "dGFn");
}
