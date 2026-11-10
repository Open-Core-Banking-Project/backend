using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Application.Products;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class AccountOpeningApprovalProfileTests
{
    [Fact]
    public void The_profile_is_pinned_to_the_approved_workflow_shape()
    {
        Assert.Equal("account.open.v1", AccountOpeningApprovalV1.WorkflowKey);
        Assert.Equal("account-open-approval-v1", AccountOpeningApprovalV1.WorkflowVersionId);
        Assert.Equal("account.application", AccountOpeningApprovalV1.SubjectType);
        Assert.Equal("core-ledger", AccountOpeningApprovalV1.SourceService);
        Assert.Equal("account.application.approve", AccountOpeningApprovalV1.RequiredPermission);
        // The same literal is pinned in the Operations Workflow profile catalogue.
        Assert.Equal(
            "ff57256f23594216f48e7044ecb97b9ad1d3149772dffb55b9ef92a32e8a7b62",
            AccountOpeningApprovalV1.WorkflowDefinitionHash);
    }

    [Fact]
    public void The_approval_request_binds_the_exact_version_and_snapshot_hash()
    {
        ApprovalRequestedV1 payload = new(
            "institution-1",
            "request-1",
            "source-key-1",
            AccountOpeningApprovalV1.WorkflowKey,
            AccountOpeningApprovalV1.WorkflowVersionId,
            AccountOpeningApprovalV1.WorkflowDefinitionHash,
            AccountOpeningApprovalV1.SourceService,
            AccountOpeningApprovalV1.SubjectType,
            "application-1",
            "2",
            new string('a', 64),
            "maker-1",
            AccountOpeningApprovalV1.RequiredPermission,
            "correlation-1",
            new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal("2", payload.SubjectVersion);
        Assert.Equal(64, payload.SubjectHash.Length);
        Assert.Equal("account.application", payload.SubjectType);
    }
}

public sealed class AccountApprovalLifecycleTests
{
    [Fact]
    public void An_eligible_application_is_submitted_for_approval()
    {
        Assert.Equal(
            AccountApplicationStatus.PendingApproval,
            AccountApplicationLifecycle.RecordSubmittedForApproval(AccountApplicationStatus.Eligible));

        Assert.Throws<InvalidOperationException>(() =>
            AccountApplicationLifecycle.RecordSubmittedForApproval(AccountApplicationStatus.Draft));
        Assert.Throws<InvalidOperationException>(() =>
            AccountApplicationLifecycle.RecordSubmittedForApproval(AccountApplicationStatus.Ineligible));
    }

    [Theory]
    [InlineData(true, AccountApplicationStatus.Approved)]
    [InlineData(false, AccountApplicationStatus.Rejected)]
    public void A_decision_applies_only_to_a_submitted_application(bool approved, AccountApplicationStatus expected)
    {
        Assert.Equal(
            expected,
            AccountApplicationLifecycle.RecordApprovalDecision(AccountApplicationStatus.PendingApproval, approved));

        Assert.Throws<InvalidOperationException>(() =>
            AccountApplicationLifecycle.RecordApprovalDecision(AccountApplicationStatus.Eligible, approved));
        Assert.Throws<InvalidOperationException>(() =>
            AccountApplicationLifecycle.RecordApprovalDecision(AccountApplicationStatus.Approved, approved));
    }

    [Fact]
    public void Activation_follows_only_an_open_account()
    {
        Assert.Equal(
            AccountApplicationStatus.Active,
            AccountApplicationLifecycle.RecordActivated(AccountApplicationStatus.Open));

        Assert.Throws<InvalidOperationException>(() =>
            AccountApplicationLifecycle.RecordActivated(AccountApplicationStatus.Approved));
        Assert.Throws<InvalidOperationException>(() =>
            AccountApplicationLifecycle.RecordActivated(AccountApplicationStatus.Active));
    }

    [Fact]
    public void Activation_uses_the_approved_three_segment_permission()
    {
        Assert.Equal("account.activation.create", AccountPermissions.ActivationCreate.Value);
        Assert.Equal("account", AccountPermissions.ActivationCreate.Domain);
        Assert.Equal("activation", AccountPermissions.ActivationCreate.Resource);
        Assert.Equal("create", AccountPermissions.ActivationCreate.Action);
        Assert.True(AccountPermissions.ActivationCreateRequirement.RequiresStepUp);
    }
}

public sealed class AccountReconciliationPolicyTests
{
    [Fact]
    public void An_open_account_without_a_proven_ledger_initialisation_is_a_finding()
    {
        IReadOnlyList<ConfigurationReconciliationFinding> findings = AccountReconciliationPolicy.Evaluate(
            [
                new AccountReconciliationFacts("account-1", "application-1", "Open", "not_initialised", null),
                new AccountReconciliationFacts("account-2", "application-2", "Active", "initialised", "ledger-2"),
            ],
            []);

        ConfigurationReconciliationFinding finding = Assert.Single(findings);
        Assert.Equal(AccountReconciliationPolicy.MissingLedgerInitialisationCode, finding.Code);
        Assert.Equal("account-1", finding.Reference);
    }

    [Fact]
    public void An_approved_application_without_an_account_is_a_finding()
    {
        IReadOnlyList<ConfigurationReconciliationFinding> findings = AccountReconciliationPolicy.Evaluate(
            [],
            [
                new ApplicationReconciliationFacts("application-1", "Approved"),
                new ApplicationReconciliationFacts("application-2", "Open"),
            ]);

        ConfigurationReconciliationFinding finding = Assert.Single(findings);
        Assert.Equal(AccountReconciliationPolicy.ApprovedWithoutAccountCode, finding.Code);
        Assert.Equal("application-1", finding.Reference);
    }

    [Fact]
    public void Findings_stay_bounded_per_code()
    {
        AccountReconciliationFacts[] accounts = Enumerable.Range(0, 60)
            .Select(index => new AccountReconciliationFacts(
                $"account-{index:D3}", $"application-{index:D3}", "Open", "not_initialised", null))
            .ToArray();

        IReadOnlyList<ConfigurationReconciliationFinding> findings = AccountReconciliationPolicy.Evaluate(accounts, []);

        Assert.Equal(ConfigurationReconciliationPolicy.MaximumFindingsPerCode, findings.Count);
    }
}

public sealed class LedgerInitialisationProofTests
{
    [Fact]
    public void A_ledger_account_carries_no_balance_amount_or_posting_field()
    {
        string[] properties = typeof(LedgerAccountRecord)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(properties, name => name.Contains("Balance", StringComparison.Ordinal));
        Assert.DoesNotContain(properties, name => name.Contains("Amount", StringComparison.Ordinal));
        Assert.DoesNotContain(properties, name => name.Contains("Posting", StringComparison.Ordinal));
        Assert.DoesNotContain(properties, name => name.Contains("Journal", StringComparison.Ordinal));
        Assert.Contains(nameof(LedgerAccountRecord.CurrencyCode), properties);
        Assert.Contains(nameof(LedgerAccountRecord.Status), properties);
    }

    [Fact]
    public void One_ledger_account_exists_per_bank_account()
    {
        using CoreLedgerDbContext context = CreateContext();
        var ledger = context.Model.FindEntityType(typeof(LedgerAccountRecord))
            ?? throw new InvalidOperationException("Ledger account mapping is missing.");

        Assert.Contains(ledger.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["InstitutionId", "AccountId"]));
        Assert.NotEmpty(ledger.GetDeclaredQueryFilters());
    }

    [Fact]
    public void An_account_records_the_approval_and_ledger_references()
    {
        using CoreLedgerDbContext context = CreateContext();
        var account = context.Model.FindEntityType(typeof(AccountRecord))!;

        Assert.Equal("ledger_account_reference", account.FindProperty(nameof(AccountRecord.LedgerAccountReference))!.GetColumnName());
        var application = context.Model.FindEntityType(typeof(AccountApplicationRecord))!;
        Assert.Equal("approval_request_id", application.FindProperty(nameof(AccountApplicationRecord.ApprovalRequestId))!.GetColumnName());
        Assert.Equal("eligibility_snapshot_hash", application.FindProperty(nameof(AccountApplicationRecord.EligibilitySnapshotHash))!.GetColumnName());
    }

    [Fact]
    public void The_activation_contract_carries_no_monetary_field()
    {
        string[] properties = typeof(AccountActivationResponseV1)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(properties, name => name.Contains("Balance", StringComparison.Ordinal));
        Assert.DoesNotContain(properties, name => name.Contains("Amount", StringComparison.Ordinal));
        Assert.Contains(nameof(AccountActivationResponseV1.LedgerAccountReference), properties);
    }

    private static CoreLedgerDbContext CreateContext()
    {
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(InstitutionId.From("institution-trusted"));
        DbContextOptions<CoreLedgerDbContext> options = new DbContextOptionsBuilder<CoreLedgerDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;******")
            .Options;
        return new CoreLedgerDbContext(options, tenantContext);
    }
}
