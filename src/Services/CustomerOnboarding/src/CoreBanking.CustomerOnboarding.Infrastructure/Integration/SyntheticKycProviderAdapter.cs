using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.CustomerOnboarding.Application.Kyc;
using CoreBanking.CustomerOnboarding.Domain.Kyc;

namespace CoreBanking.CustomerOnboarding.Infrastructure.Integration;

/// <summary>
/// The deterministic synthetic provider script. It defines no KYC rule: it only reproduces the transport
/// situations an approved provider can produce. Decision: P1-E03-D07, P1-E03-D08.
/// </summary>
public sealed record SyntheticKycProviderScript
{
    public string ProviderRouteId { get; init; } = "synthetic-kyc-route-1";

    /// <summary>Submissions refused before acceptance, so a retry is safe.</summary>
    public int UnavailableSubmissionsBeforeAccept { get; init; }

    /// <summary>Submissions the provider accepted but answered late, so the outcome is unknown.</summary>
    public int TimedOutSubmissionsBeforeAccept { get; init; }

    /// <summary>Enquiries answered as pending before the canonical result is available.</summary>
    public int PendingEnquiriesBeforeResult { get; init; }

    public CanonicalVerificationOutcome Outcome { get; init; } = CanonicalVerificationOutcome.Passed;

    public string ProviderMappingVersion { get; init; } = "synthetic-kyc-mapping-v1";

    public static SyntheticKycProviderScript Passed { get; } = new();

    public static SyntheticKycProviderScript Failed { get; } = new() { Outcome = CanonicalVerificationOutcome.Failed };
}

/// <summary>
/// The only provider adapter bound in Phase 1. It is deterministic, uses no network and never decides a KYC
/// outcome: the script supplies it. Decision: P1-E03-D07, P1-E03-D08.
/// </summary>
public sealed class SyntheticKycProviderAdapter : IKycProviderPort
{
    private readonly SyntheticKycProviderScript _script;
    private readonly ConcurrentDictionary<string, int> _submissions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, VerificationState> _accepted = new(StringComparer.Ordinal);

    public SyntheticKycProviderAdapter(SyntheticKycProviderScript script)
    {
        _script = script ?? throw new ArgumentNullException(nameof(script));
    }

    public string RouteFor(string checkCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkCode);
        return _script.ProviderRouteId;
    }

    public Task<KycProviderReceipt> SubmitAsync(KycProviderSubmission submission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentException.ThrowIfNullOrWhiteSpace(submission.AttemptId);

        // The script counts submissions per verification, so a retry after a refusal is a new attempt of the
        // same verification rather than a fresh script position.
        int submissionNumber = _submissions.AddOrUpdate(
            submission.InstitutionId + "|" + submission.VerificationId,
            1,
            (_, current) => current + 1);

        if (submissionNumber <= _script.UnavailableSubmissionsBeforeAccept)
        {
            return Task.FromResult(new KycProviderReceipt(
                KycProviderReceiptStatus.Unavailable,
                _script.ProviderRouteId,
                ProviderReference: null));
        }

        string providerReference = "syn-" + submission.AttemptId[..Math.Min(20, submission.AttemptId.Length)];
        bool late = submissionNumber <= _script.UnavailableSubmissionsBeforeAccept + _script.TimedOutSubmissionsBeforeAccept;

        _accepted.TryAdd(submission.InstitutionId + "|" + submission.AttemptId, new VerificationState());

        return Task.FromResult(new KycProviderReceipt(
            late ? KycProviderReceiptStatus.TimedOut : KycProviderReceiptStatus.Accepted,
            _script.ProviderRouteId,
            providerReference));
    }

    public Task<KycProviderResult> EnquireAsync(KycProviderEnquiry enquiry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enquiry);
        ArgumentException.ThrowIfNullOrWhiteSpace(enquiry.AttemptId);

        if (!_accepted.TryGetValue(enquiry.InstitutionId + "|" + enquiry.AttemptId, out VerificationState? state))
        {
            return Task.FromResult(new KycProviderResult(
                KycProviderEnquiryStatus.Unknown,
                Outcome: null,
                ProviderMappingVersion: null,
                EvidenceReference: null,
                EvidenceHash: null));
        }

        int enquiryNumber;
        lock (state)
        {
            enquiryNumber = ++state.Enquiries;
        }

        if (enquiryNumber <= _script.PendingEnquiriesBeforeResult)
        {
            return Task.FromResult(new KycProviderResult(
                KycProviderEnquiryStatus.Pending,
                Outcome: null,
                ProviderMappingVersion: null,
                EvidenceReference: null,
                EvidenceHash: null));
        }

        string evidenceReference = "syn-evidence-" + enquiry.AttemptId[..Math.Min(24, enquiry.AttemptId.Length)];
        string evidenceHash = EvidenceHashFor(enquiry.InstitutionId, enquiry.AttemptId, _script.Outcome);

        return Task.FromResult(new KycProviderResult(
            KycProviderEnquiryStatus.Completed,
            _script.Outcome,
            _script.ProviderMappingVersion,
            evidenceReference,
            evidenceHash));
    }

    /// <summary>A stable digest of the synthetic evidence, so a consumer can verify what it received.</summary>
    public static string EvidenceHashFor(string institutionId, string attemptId, CanonicalVerificationOutcome outcome)
    {
        string material = string.Create(
            CultureInfo.InvariantCulture,
            $"synthetic-kyc|{institutionId}|{attemptId}|{outcome}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private sealed class VerificationState
    {
        public int Enquiries { get; set; }
    }
}
