using System;
using System.Globalization;

namespace CoreBanking.CoreLedger.Domain.Products;

public sealed record SavingsEligibilityApprovalBinding(
    string Status,
    string RequestId,
    string VersionId,
    int VersionNumber,
    string ContentHash);

public static class SavingsEligibilityApprovalApplication
{
    public static string Apply(
        SavingsEligibilityApprovalBinding binding,
        string requestId,
        string subjectId,
        string subjectVersion,
        string subjectHash,
        string decision)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(binding.Status, "submitted", StringComparison.Ordinal)
            || !string.Equals(binding.RequestId, requestId, StringComparison.Ordinal)
            || !string.Equals(binding.VersionId, subjectId, StringComparison.Ordinal)
            || !string.Equals(binding.VersionNumber.ToString(CultureInfo.InvariantCulture), subjectVersion, StringComparison.Ordinal)
            || !string.Equals(binding.ContentHash, subjectHash, StringComparison.Ordinal))
            throw new InvalidOperationException("approval_decision_subject_mismatch");

        return decision switch
        {
            "approved" => "approved",
            "rejected" => "draft",
            _ => throw new InvalidOperationException("approval_decision_unsupported"),
        };
    }
}
