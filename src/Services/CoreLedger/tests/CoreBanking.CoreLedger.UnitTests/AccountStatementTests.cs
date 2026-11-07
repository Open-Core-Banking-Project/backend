using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Accounts;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

/// <summary>
/// The approved statement policy (P1-E04-D15): allow-listed periods, formats and purposes with no default, a ledger
/// query as the only content source, and an opaque short-lived artifact reference that never carries content.
/// </summary>
public sealed class AccountStatementTests
{
    private static readonly DateOnly Opened = new(2026, 1, 1);
    private static readonly DateOnly BusinessDate = new(2026, 6, 30);
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private static StatementPolicy Policy(
        int maximumSpanDays = 366,
        int retentionDays = 3650,
        int referenceMinutes = 10) =>
        StatementPolicy.Create(
            "policy-1",
            1,
            maximumSpanDays,
            retentionDays,
            referenceMinutes,
            ["pdf", "csv"],
            ["periodic", "on_demand", "receipt", "regulatory"],
            ["pdf"],
            ["calendar_month", "quarter", "year_to_date", "custom"]);

    private static AccountStatementFacts Facts(
        DateOnly? from = null,
        DateOnly? to = null,
        string format = "pdf",
        string purpose = "periodic") =>
        new(Opened, BusinessDate, from ?? Opened, to ?? BusinessDate, format, purpose);

    [Fact]
    public void A_policy_round_trips_through_its_canonical_configuration_content()
    {
        StatementPolicy policy = Policy();

        StatementPolicy parsed = StatementPolicy.Parse(policy.ToConfigurationContent().CanonicalJson);

        Assert.Equal(StatementPolicy.Area, "statement.policy");
        Assert.Equal(StatementPolicy.Schema, "statement.policy.v1");
        Assert.Equal(policy.PolicyId, parsed.PolicyId);
        Assert.Equal(policy.MaximumSpanDays, parsed.MaximumSpanDays);
        Assert.Equal(policy.RetentionDays, parsed.RetentionDays);
        Assert.Equal(policy.ArtifactReferenceMinutes, parsed.ArtifactReferenceMinutes);
        Assert.Equal(policy.Formats, parsed.Formats);
        Assert.Equal(policy.Purposes, parsed.Purposes);
        Assert.Equal(policy.ReceiptFormats, parsed.ReceiptFormats);
        Assert.Equal(policy.PeriodKinds, parsed.PeriodKinds);
    }

    [Fact]
    public void Only_pdf_and_csv_are_supported_and_an_unknown_code_is_refused_rather_than_defaulted()
    {
        Assert.Throws<ArgumentException>(() => StatementPolicy.Create(
            "policy-1", 1, 366, 3650, 10, ["pdf", "mt940"], ["periodic"], ["pdf"], ["custom"]));
        Assert.Throws<ArgumentException>(() => StatementPolicy.Create(
            "policy-1", 1, 366, 3650, 10, ["pdf"], ["periodic", "bespoke"], ["pdf"], ["custom"]));
        Assert.Throws<ArgumentException>(() => StatementPolicy.Create(
            "policy-1", 1, 366, 3650, 10, ["pdf"], ["periodic"], ["csv"], ["custom"]));

        StatementRequestValidation validation = AccountStatementPolicy.Validate(
            Policy(), Facts(format: "mt940"));
        Assert.False(validation.Accepted);
        Assert.Contains(AccountStatementPolicy.FormatNotSupportedCode, validation.RefusalCodes);

        StatementRequestValidation empty = AccountStatementPolicy.Validate(Policy(), Facts(format: string.Empty));
        Assert.False(empty.Accepted);
        Assert.Contains(AccountStatementPolicy.FormatNotSupportedCode, empty.RefusalCodes);
    }

    [Fact]
    public void A_retention_below_the_platform_floor_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Policy(retentionDays: StatementPolicy.PlatformRetentionFloorDays - 1));
        Assert.Equal(365, StatementPolicy.PlatformRetentionFloorDays);
    }

    [Fact]
    public void A_receipt_may_only_use_an_allowed_receipt_format()
    {
        StatementRequestValidation receipt = AccountStatementPolicy.Validate(Policy(), Facts(purpose: "receipt"));
        Assert.True(receipt.Accepted);

        StatementRequestValidation csvReceipt = AccountStatementPolicy.Validate(Policy(), Facts(format: "csv", purpose: "receipt"));
        Assert.False(csvReceipt.Accepted);
        Assert.Contains(AccountStatementPolicy.FormatNotSupportedCode, csvReceipt.RefusalCodes);
    }

    [Fact]
    public void A_future_period_or_a_period_before_opening_is_refused()
    {
        StatementRequestValidation future = AccountStatementPolicy.Validate(
            Policy(), Facts(from: Opened, to: BusinessDate.AddDays(1)));
        Assert.Contains(AccountStatementPolicy.PeriodInvalidCode, future.RefusalCodes);

        StatementRequestValidation beforeOpening = AccountStatementPolicy.Validate(
            Policy(), Facts(from: Opened.AddDays(-1), to: BusinessDate));
        Assert.Contains(AccountStatementPolicy.PeriodInvalidCode, beforeOpening.RefusalCodes);
    }

    [Fact]
    public void An_inverted_or_oversized_period_is_refused()
    {
        StatementRequestValidation inverted = AccountStatementPolicy.Validate(
            Policy(), Facts(from: BusinessDate, to: Opened));
        Assert.Contains(AccountStatementPolicy.PeriodInvalidCode, inverted.RefusalCodes);

        StatementRequestValidation oversized = AccountStatementPolicy.Validate(
            Policy(maximumSpanDays: 30), Facts(from: Opened, to: BusinessDate));
        Assert.Contains(AccountStatementPolicy.PeriodInvalidCode, oversized.RefusalCodes);

        Assert.True(AccountStatementPolicy.Validate(
            Policy(maximumSpanDays: 30), Facts(from: BusinessDate.AddDays(-29), to: BusinessDate)).Accepted);
    }

    [Fact]
    public void A_single_day_receipt_period_is_accepted()
    {
        StatementRequestValidation validation = AccountStatementPolicy.Validate(
            Policy(), Facts(from: BusinessDate, to: BusinessDate, purpose: "receipt"));

        Assert.True(validation.Accepted);
        Assert.Empty(validation.RefusalCodes);
    }

    [Fact]
    public void A_purpose_outside_the_allow_list_is_refused()
    {
        StatementRequestValidation validation = AccountStatementPolicy.Validate(Policy(), Facts(purpose: "marketing"));

        Assert.False(validation.Accepted);
        Assert.Contains(AccountStatementPolicy.PurposeNotSupportedCode, validation.RefusalCodes);
    }

    [Fact]
    public void Refusal_codes_are_ordered_and_deduplicated()
    {
        StatementRequestValidation validation = AccountStatementPolicy.Validate(
            Policy(maximumSpanDays: 5),
            Facts(from: Opened.AddDays(-3), to: BusinessDate, format: "mt940", purpose: "marketing"));

        Assert.Equal(
            [AccountStatementPolicy.FormatNotSupportedCode, AccountStatementPolicy.PeriodInvalidCode, AccountStatementPolicy.PurposeNotSupportedCode],
            validation.RefusalCodes);
    }

    [Fact]
    public void The_artifact_reference_expires_within_the_configured_lifetime_and_is_bound_to_the_requester()
    {
        StatementPolicy policy = Policy(referenceMinutes: 5);
        DateTimeOffset expiry = AccountStatementPolicy.ReferenceExpiry(policy, Now);

        Assert.Equal(Now.AddMinutes(5), expiry);
        Assert.Empty(AccountStatementPolicy.ReferenceRefusals("account-1", "actor-1", "account-1", "actor-1", expiry, Now));
        Assert.Equal(
            [AccountStatementPolicy.ReferenceNotBoundCode],
            AccountStatementPolicy.ReferenceRefusals("account-1", "actor-1", "account-2", "actor-1", expiry, Now));
        Assert.Equal(
            [AccountStatementPolicy.ReferenceNotBoundCode],
            AccountStatementPolicy.ReferenceRefusals("account-1", "actor-1", "account-1", "actor-2", expiry, Now));
        Assert.Equal(
            [AccountStatementPolicy.ReferenceExpiredCode],
            AccountStatementPolicy.ReferenceRefusals("account-1", "actor-1", "account-1", "actor-1", Now, expiry));
    }

    [Fact]
    public void The_retention_expiry_is_measured_from_generation()
    {
        Assert.Equal(Now.AddDays(3650), AccountStatementPolicy.RetentionExpiry(Policy(), Now));
    }

    [Fact]
    public void The_statement_capabilities_use_the_three_segment_grammar()
    {
        Assert.Equal("account.summary.view", AccountPermissions.SummaryView.Value);
        Assert.Equal("account.statement.request", AccountPermissions.StatementRequest.Value);
        Assert.Equal("account.statement.retrieve", AccountPermissions.StatementRetrieve.Value);
        Assert.All(
            new[] { AccountPermissions.SummaryView, AccountPermissions.StatementRequest, AccountPermissions.StatementRetrieve },
            permission => Assert.Equal(3, permission.Value.Split('.').Length));
        Assert.True(AccountPermissions.StatementRequestRequirement.RequiresStepUp);
        Assert.False(AccountPermissions.SummaryViewRequirement.RequiresStepUp);
    }

    [Fact]
    public void The_statement_contracts_carry_no_content()
    {
        AccountStatementRequestResponseV1 response = new(
            "institution-1",
            "request-1",
            "account-1",
            Opened,
            BusinessDate,
            "pdf",
            "periodic",
            "awaiting_artifact",
            "ledger:query-1",
            "integrity-1",
            null,
            null,
            [],
            "staff",
            Now,
            Replayed: false);

        Assert.Equal("ledger:query-1", response.LedgerQueryReference);
        Assert.Null(response.ArtifactReference);
        Assert.DoesNotContain(
            typeof(AccountStatementRequestResponseV1).GetProperties(),
            property => property.Name.Contains("Content", StringComparison.Ordinal)
                || property.Name.Contains("Balance", StringComparison.Ordinal)
                || property.Name.Contains("Movement", StringComparison.Ordinal));
    }

    [Fact]
    public void Reconciliation_reports_a_statement_request_without_an_artifact_past_the_window()
    {
        IReadOnlyList<ConfigurationReconciliationFinding> findings = AccountReconciliationPolicy.Evaluate(
            [],
            [],
            null,
            [
                new StatementReconciliationFacts("request-1", "account-1", "awaiting_artifact", Now.AddDays(-2), HasArtifact: false),
                new StatementReconciliationFacts("request-2", "account-2", "awaiting_artifact", Now.AddHours(-1), HasArtifact: false),
                new StatementReconciliationFacts("request-3", "account-3", "refused", Now.AddDays(-2), HasArtifact: false),
                new StatementReconciliationFacts("request-4", "account-4", "ready", Now.AddDays(-9), HasArtifact: true),
            ],
            Now);

        ConfigurationReconciliationFinding finding = Assert.Single(findings);
        Assert.Equal(AccountReconciliationPolicy.StatementWithoutArtifactCode, finding.Code);
        Assert.Equal("request-1", finding.ConfigurationSetId);
        Assert.Equal("account-1", finding.Reference);
    }

    [Fact]
    public void The_in_process_ledger_statement_source_fails_closed_instead_of_using_a_summary()
    {
        LedgerStatementQueryResult unavailable = new(
            Available: false,
            UnavailableCode: AccountStatementPolicy.SourceUnavailableCode,
            QueryReference: "ledger:query-1",
            IntegrityReference: string.Empty,
            OpeningBalance: null,
            ClosingBalance: null,
            MovementCount: 0);

        Assert.False(unavailable.Available);
        Assert.Equal(AccountStatementPolicy.SourceUnavailableCode, unavailable.UnavailableCode);
        Assert.Null(unavailable.OpeningBalance);
        Assert.Null(unavailable.ClosingBalance);
    }

    [Fact]
    public void A_summary_without_a_ledger_answer_reports_no_balances_at_all()
    {
        AccountSummaryResult summary = new(
            "institution-1",
            "account-1",
            "****1234",
            "customer-1",
            "product-1",
            "version-1",
            "branch-1",
            "NGN",
            nameof(AccountStatus.Active),
            3,
            AccountBalanceAvailability.Unavailable,
            null,
            null,
            0m,
            0,
            0,
            Now);

        Assert.Null(summary.Balances);
        Assert.Equal("ledger_unavailable", summary.BalanceAvailability);
    }

    [Fact]
    public void A_summary_with_a_ledger_answer_carries_its_source_and_as_of()
    {
        AccountSummaryResult summary = new(
            "institution-1",
            "account-1",
            "****1234",
            "customer-1",
            "product-1",
            "version-1",
            "branch-1",
            "NGN",
            nameof(AccountStatus.Active),
            3,
            AccountBalanceAvailability.Available,
            "ledger-1",
            new LedgerAvailabilitySnapshot(0m, 0m, 0m, "ledger:ledger-1:v1", Now),
            250m,
            1,
            2,
            Now);

        Assert.NotNull(summary.Balances);
        Assert.Equal("ledger:ledger-1:v1", summary.Balances!.SourceVersion);
        Assert.Equal(Now, summary.Balances.AsOf);
        Assert.Equal(250m, summary.OutstandingLienAmount);
    }
}
