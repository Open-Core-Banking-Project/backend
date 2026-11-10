using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CoreBanking.CoreLedger.Domain.Institutions;

namespace CoreBanking.CoreLedger.Domain.Accounts;

/// <summary>
/// The institution's effective-dated <c>statement.policy</c> content: the allowed period kinds, the maximum span,
/// the format and purpose allow-lists, the artifact-reference lifetime and the retention period. No default policy
/// is shipped: without a published policy a request is refused. Decision: P1-E04-D15.
/// </summary>
public sealed record StatementPolicy(
    string PolicyId,
    int Version,
    int MaximumSpanDays,
    int RetentionDays,
    int ArtifactReferenceMinutes,
    IReadOnlyList<string> Formats,
    IReadOnlyList<string> Purposes,
    IReadOnlyList<string> ReceiptFormats,
    IReadOnlyList<string> PeriodKinds)
{
    public const string Area = "statement.policy";
    public const string Schema = "statement.policy.v1";

    /// <summary>
    /// Phase 1 accepts exactly these formats; an unknown code is refused, never defaulted. Decision: P1-E04-D15.
    /// </summary>
    public static IReadOnlySet<string> SupportedFormats { get; } =
        new HashSet<string>(["pdf", "csv"], StringComparer.Ordinal);

    public static IReadOnlySet<string> SupportedPurposes { get; } =
        new HashSet<string>(["periodic", "on_demand", "receipt", "regulatory"], StringComparer.Ordinal);

    public static IReadOnlySet<string> SupportedPeriodKinds { get; } =
        new HashSet<string>(["calendar_month", "quarter", "year_to_date", "custom"], StringComparer.Ordinal);

    /// <summary>
    /// The platform floor that stops an institution configuring near-zero retention. The legal floor itself is
    /// deferred to Compliance (D15 section 6); this constant is a guard, not a policy value.
    /// </summary>
    public const int PlatformRetentionFloorDays = 365;

    private const int MaximumSpanCeilingDays = 3660;
    private const int MaximumRetentionDays = 36_500;
    private const int MaximumReferenceMinutes = 60;

    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        "policyId", "version", "maximumSpanDays", "retentionDays", "artifactReferenceMinutes",
        "formats", "purposes", "receiptFormats", "periodKinds",
    };

    public ConfigurationContent ToConfigurationContent() => ConfigurationContent.Create(JsonSerializer.Serialize(new
    {
        policyId = PolicyId,
        version = Version,
        maximumSpanDays = MaximumSpanDays,
        retentionDays = RetentionDays,
        artifactReferenceMinutes = ArtifactReferenceMinutes,
        formats = Formats.ToArray(),
        purposes = Purposes.ToArray(),
        receiptFormats = ReceiptFormats.ToArray(),
        periodKinds = PeriodKinds.ToArray(),
    }));

    public bool AcceptsFormat(string formatCode, string purposeCode) =>
        Formats.Contains(formatCode?.Trim().ToLowerInvariant() ?? string.Empty, StringComparer.Ordinal)
        && (string.Equals(purposeCode?.Trim().ToLowerInvariant(), "receipt", StringComparison.Ordinal)
            ? ReceiptFormats.Contains(formatCode?.Trim().ToLowerInvariant() ?? string.Empty, StringComparer.Ordinal)
            : true);

    public static StatementPolicy Create(
        string policyId,
        int version,
        int maximumSpanDays,
        int retentionDays,
        int artifactReferenceMinutes,
        IEnumerable<string> formats,
        IEnumerable<string> purposes,
        IEnumerable<string> receiptFormats,
        IEnumerable<string> periodKinds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyId);
        if (policyId.Trim().Length > 64)
            throw new ArgumentException("A policy identifier may contain at most 64 characters.", nameof(policyId));
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        if (maximumSpanDays < 1 || maximumSpanDays > MaximumSpanCeilingDays)
            throw new ArgumentOutOfRangeException(
                nameof(maximumSpanDays),
                $"A maximum statement span must be between 1 and {MaximumSpanCeilingDays} days.");
        if (retentionDays < PlatformRetentionFloorDays || retentionDays > MaximumRetentionDays)
            throw new ArgumentOutOfRangeException(
                nameof(retentionDays),
                $"A statement retention period must be at least {PlatformRetentionFloorDays} days and at most {MaximumRetentionDays} days.");
        if (artifactReferenceMinutes < 1 || artifactReferenceMinutes > MaximumReferenceMinutes)
            throw new ArgumentOutOfRangeException(
                nameof(artifactReferenceMinutes),
                $"An artifact reference lifetime must be between 1 and {MaximumReferenceMinutes} minutes.");

        string[] allowedFormats = ValidateCodes(formats, SupportedFormats, nameof(formats));
        string[] allowedPurposes = ValidateCodes(purposes, SupportedPurposes, nameof(purposes));
        string[] allowedKinds = ValidateCodes(periodKinds, SupportedPeriodKinds, nameof(periodKinds));
        string[] receipts = ValidateCodes(receiptFormats, SupportedFormats, nameof(receiptFormats));
        if (receipts.Any(format => !allowedFormats.Contains(format, StringComparer.Ordinal)))
            throw new ArgumentException("A receipt format must also be an allowed format.", nameof(receiptFormats));

        return new StatementPolicy(
            policyId.Trim(),
            version,
            maximumSpanDays,
            retentionDays,
            artifactReferenceMinutes,
            allowedFormats,
            allowedPurposes,
            receipts,
            allowedKinds);
    }

    public static StatementPolicy Parse(string contentJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentJson);

        using JsonDocument document = JsonDocument.Parse(contentJson);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Any(property => !AllowedProperties.Contains(property.Name)))
            throw new ArgumentException("The statement policy contains unsupported properties.", nameof(contentJson));

        foreach (string required in AllowedProperties)
        {
            if (!root.TryGetProperty(required, out _))
                throw new ArgumentException("The statement policy is missing a required property.", nameof(contentJson));
        }

        return Create(
            root.GetProperty("policyId").GetString() ?? string.Empty,
            root.GetProperty("version").GetInt32(),
            root.GetProperty("maximumSpanDays").GetInt32(),
            root.GetProperty("retentionDays").GetInt32(),
            root.GetProperty("artifactReferenceMinutes").GetInt32(),
            ReadCodes(root, "formats"),
            ReadCodes(root, "purposes"),
            ReadCodes(root, "receiptFormats"),
            ReadCodes(root, "periodKinds"));
    }

    private static string[] ReadCodes(JsonElement root, string property)
    {
        if (root.GetProperty(property).ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"The '{property}' property must be an array.", nameof(root));

        return [.. root.GetProperty(property).EnumerateArray().Select(element =>
            element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : throw new ArgumentException($"The '{property}' property must contain strings.", nameof(root)))];
    }

    private static string[] ValidateCodes(
        IEnumerable<string> codes,
        IReadOnlySet<string> allowed,
        string parameterName)
    {
        string[] normalised = [.. (codes ?? throw new ArgumentNullException(parameterName))
            .Select(code => code?.Trim().ToLowerInvariant() ?? string.Empty)];
        if (normalised.Length == 0)
            throw new ArgumentException("At least one code is required.", parameterName);
        if (normalised.Distinct(StringComparer.Ordinal).Count() != normalised.Length)
            throw new ArgumentException("A code may appear only once.", parameterName);
        if (normalised.Any(code => !allowed.Contains(code)))
            throw new ArgumentException("The code is not approved for Phase 1.", parameterName);
        return normalised;
    }
}

/// <summary>One authorised statement request as the policy and the ledger answer see it.</summary>
public sealed record AccountStatementFacts(
    DateOnly AccountOpenedOn,
    DateOnly BusinessDate,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string FormatCode,
    string PurposeCode);

public sealed record StatementRequestValidation(bool Accepted, IReadOnlyList<string> RefusalCodes);

/// <summary>
/// The approved statement request rules: a period inside the account's life and the business date, no span beyond
/// the configured maximum, and only an allow-listed format and purpose with no default. Decision: P1-E04-D15.
/// </summary>
public static class AccountStatementPolicy
{
    public const string PolicyMissingCode = "statement_policy_missing";
    public const string FormatNotSupportedCode = "statement_format_not_supported";
    public const string PurposeNotSupportedCode = "statement_purpose_not_supported";
    public const string PeriodInvalidCode = "statement_period_invalid";
    public const string SourceUnavailableCode = "statement_source_unavailable";
    public const string WithoutArtifactCode = "statement_request_without_artifact";
    public const string ReferenceExpiredCode = "statement_artifact_reference_expired";
    public const string ReferenceNotBoundCode = "statement_artifact_reference_not_bound";

    public static StatementRequestValidation Validate(StatementPolicy policy, AccountStatementFacts facts)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(facts);

        List<string> codes = [];
        string format = facts.FormatCode?.Trim().ToLowerInvariant() ?? string.Empty;
        string purpose = facts.PurposeCode?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!policy.Purposes.Contains(purpose, StringComparer.Ordinal))
        {
            codes.Add(PurposeNotSupportedCode);
        }

        if (!StatementPolicy.SupportedFormats.Contains(format)
            || !policy.AcceptsFormat(format, purpose))
        {
            codes.Add(FormatNotSupportedCode);
        }

        if (facts.PeriodFrom < facts.AccountOpenedOn
            || facts.PeriodTo > facts.BusinessDate
            || facts.PeriodTo < facts.PeriodFrom
            || facts.PeriodTo.DayNumber - facts.PeriodFrom.DayNumber + 1 > policy.MaximumSpanDays)
        {
            codes.Add(PeriodInvalidCode);
        }

        string[] ordered = [.. codes.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal)];
        return new StatementRequestValidation(ordered.Length == 0, ordered);
    }

    /// <summary>
    /// An artifact reference is opaque, short-lived and bound to the requesting party and the account. An
    /// unauthorised party learns nothing about whether the artifact exists. Decision: P1-E04-D15.
    /// </summary>
    public static IReadOnlyList<string> ReferenceRefusals(
        string referenceAccountId,
        string referenceRequesterId,
        string callerAccountId,
        string callerId,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(referenceAccountId);
        ArgumentNullException.ThrowIfNull(callerAccountId);

        bool bound = string.Equals(referenceAccountId, callerAccountId?.Trim(), StringComparison.Ordinal)
            && string.Equals(referenceRequesterId, callerId?.Trim(), StringComparison.Ordinal);
        if (!bound)
        {
            return [ReferenceNotBoundCode];
        }

        return expiresAt <= now ? [ReferenceExpiredCode] : [];
    }

    /// <summary>The signed reference lifetime in minutes cannot exceed the configured maximum.</summary>
    public static DateTimeOffset ReferenceExpiry(StatementPolicy policy, DateTimeOffset issuedAt) =>
        issuedAt.AddMinutes(policy.ArtifactReferenceMinutes);

    /// <summary>The retention expiry is measured from generation and is never shortened by a later policy.</summary>
    public static DateTimeOffset RetentionExpiry(StatementPolicy policy, DateTimeOffset generatedAt) =>
        generatedAt.AddDays(policy.RetentionDays);
}
