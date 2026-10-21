using System;
using CoreBanking.CoreLedger.Domain.Products;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class SavingsEligibilityApprovalApplicationTests
{
    private static readonly SavingsEligibilityApprovalBinding Binding = new(
        "submitted", "request-1", "version-1", 4, new string('a', 64));

    [Theory]
    [InlineData("approved", "approved")]
    [InlineData("rejected", "draft")]
    public void Exact_decision_applies_the_expected_source_state(string decision, string expected) =>
        Assert.Equal(expected, SavingsEligibilityApprovalApplication.Apply(
            Binding, "request-1", "version-1", "4", new string('a', 64), decision));

    [Theory]
    [InlineData("request-2", "version-1", "4")]
    [InlineData("request-1", "version-2", "4")]
    [InlineData("request-1", "version-1", "5")]
    public void Decision_cannot_apply_to_a_different_request_or_version(
        string requestId,
        string versionId,
        string versionNumber) =>
        Assert.Equal("approval_decision_subject_mismatch", Assert.Throws<InvalidOperationException>(() =>
            SavingsEligibilityApprovalApplication.Apply(
                Binding, requestId, versionId, versionNumber, new string('a', 64), "approved")).Message);

    [Fact]
    public void Decision_cannot_apply_to_a_different_content_hash() =>
        Assert.Equal("approval_decision_subject_mismatch", Assert.Throws<InvalidOperationException>(() =>
            SavingsEligibilityApprovalApplication.Apply(
                Binding, "request-1", "version-1", "4", new string('b', 64), "approved")).Message);
}
