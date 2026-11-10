using System;
using System.Collections.Generic;
using System.Linq;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CoreLedger.Application.Accounts;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Accounts;
using CoreBanking.CoreLedger.Domain.Institutions;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class AccountNumberSchemeTests
{
    private static AccountNumberScheme Scheme(
        string prefix = "10",
        int productWidth = 0,
        int branchWidth = 0,
        AccountNumberCheckDigit checkDigit = AccountNumberCheckDigit.Mod10Luhn) =>
        AccountNumberScheme.Create("savings-default", 1, prefix, productWidth, branchWidth, 6, checkDigit);

    [Fact]
    public void The_scheme_round_trips_through_its_canonical_content()
    {
        AccountNumberScheme scheme = Scheme(productWidth: 2, branchWidth: 3);

        CoreBanking.CoreLedger.Domain.Institutions.ConfigurationContent content = scheme.ToConfigurationContent();

        Assert.Equal(scheme, AccountNumberScheme.Parse(content.CanonicalJson));
        Assert.Equal(content.Hash, CoreBanking.CoreLedger.Domain.Institutions.ConfigurationContent
            .Create(content.CanonicalJson).Hash);
    }

    [Theory]
    [InlineData("{\"schemeId\":\"s\",\"version\":1,\"prefix\":\"10\",\"productCodeWidth\":0,\"branchCodeWidth\":0,\"sequenceWidth\":5,\"checkDigit\":\"none\"}")]
    [InlineData("{\"schemeId\":\"s\",\"version\":1,\"prefix\":\"1234567\",\"productCodeWidth\":0,\"branchCodeWidth\":0,\"sequenceWidth\":6,\"checkDigit\":\"none\"}")]
    [InlineData("{\"schemeId\":\"s\",\"version\":0,\"prefix\":\"10\",\"productCodeWidth\":0,\"branchCodeWidth\":0,\"sequenceWidth\":6,\"checkDigit\":\"none\"}")]
    [InlineData("{\"schemeId\":\"s\",\"version\":1,\"prefix\":\"10\",\"productCodeWidth\":0,\"branchCodeWidth\":0,\"sequenceWidth\":6,\"checkDigit\":\"crc\"}")]
    [InlineData("{\"schemeId\":\"s\",\"version\":1,\"prefix\":\"10\",\"productCodeWidth\":0,\"branchCodeWidth\":0,\"sequenceWidth\":6,\"checkDigit\":\"none\",\"template\":\"{seq}\"}")]
    [InlineData("{\"schemeId\":\"s\",\"version\":1,\"prefix\":\"10\",\"productCodeWidth\":0,\"branchCodeWidth\":0,\"sequenceWidth\":6}")]
    public void An_invalid_scheme_document_is_refused(string json)
    {
        Assert.ThrowsAny<ArgumentException>(() => AccountNumberScheme.Parse(json));
    }

    [Fact]
    public void The_number_is_built_from_the_declared_segments_and_the_stored_sequence()
    {
        AccountNumberScheme scheme = Scheme(prefix: "99", productWidth: 2, branchWidth: 3);

        string number = scheme.Format("07", "012", 42);

        Assert.Equal("99" + "07" + "012" + "000042", number[..^1]);
        Assert.Equal(14, number.Length);
        Assert.True(scheme.Verify(number));
    }

    [Fact]
    public void A_tampered_number_fails_its_check_digit()
    {
        AccountNumberScheme scheme = Scheme();
        string number = scheme.Format("", "", 42);
        string tampered = string.Concat(number[..^2], number[^2] == '9' ? "8" : "9", number[^1..]);

        Assert.True(scheme.Verify(number));
        Assert.False(scheme.Verify(tampered));
    }

    [Fact]
    public void A_scheme_without_a_check_digit_never_claims_verification()
    {
        AccountNumberScheme scheme = Scheme(checkDigit: AccountNumberCheckDigit.None);

        string number = scheme.Format("", "", 7);

        Assert.Equal("10" + "000007", number);
        Assert.False(scheme.Verify(number));
    }

    [Fact]
    public void The_mask_keeps_four_significant_digits_and_hides_the_rest()
    {
        AccountNumberScheme scheme = Scheme(prefix: "10", checkDigit: AccountNumberCheckDigit.Mod10Luhn);
        string number = scheme.Format("", "", 123456);

        string masked = scheme.Mask(number);

        Assert.Equal("****" + number.Substring(number.Length - 5, 4) + number[^1], masked);
        Assert.DoesNotContain("1234", masked, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("0700")]
    [InlineData("AB")]
    public void A_product_segment_that_does_not_match_the_codes_is_refused(string code)
    {
        AccountNumberScheme scheme = Scheme(productWidth: 3);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => scheme.Format(code, string.Empty, 1));

        Assert.Equal("account_number_product_segment_not_numeric", exception.Message);
    }

    [Fact]
    public void Capacity_matches_the_declared_sequence_width()
    {
        Assert.Equal(1_000_000, Scheme().Capacity);
        Assert.Equal(
            1_000_000_000_000,
            AccountNumberScheme.Create("s", 1, "10", 0, 0, 12, AccountNumberCheckDigit.None).Capacity);
    }
}

public sealed class AccountNumberAllocationPolicyTests
{
    [Fact]
    public void Allocation_uses_the_stored_next_value_and_never_the_highest_account_number()
    {
        AccountNumberAllocationState state = new(NextValue: 42, HighWaterMark: 41, Version: 7);

        AccountNumberAllocation allocation = AccountNumberAllocationPolicy.Allocate(state, 1, 1_000_000);

        Assert.Equal([42L], allocation.Sequences);
        Assert.Equal(43, allocation.State.NextValue);
        Assert.Equal(43, allocation.State.HighWaterMark);
        Assert.Equal(8, allocation.State.Version);
    }

    [Fact]
    public void A_request_beyond_the_remaining_capacity_is_refused_with_the_stable_code()
    {
        AccountNumberAllocationState state = new(NextValue: 999_999, HighWaterMark: 999_998, Version: 1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AccountNumberAllocationPolicy.Allocate(state, 2, 1_000_000));

        Assert.Equal(AccountNumberAllocationPolicy.ExhaustedCode, exception.Message);
    }

    [Fact]
    public void The_final_number_still_allocates()
    {
        AccountNumberAllocationState state = new(NextValue: 999_999, HighWaterMark: 999_998, Version: 1);

        AccountNumberAllocation allocation = AccountNumberAllocationPolicy.Allocate(state, 1, 1_000_000);

        Assert.Equal(999_999, allocation.Sequences[0]);
        Assert.Equal(1_000_000, allocation.State.NextValue);
        Assert.Equal(95, allocation.DepletionBand);
        Assert.Equal(0, AccountNumberAllocationPolicy.Available(allocation.State, 1_000_000));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(799_999, 0)]
    [InlineData(800_000, 80)]
    [InlineData(900_000, 90)]
    [InlineData(950_000, 95)]
    public void Depletion_bands_follow_the_approved_thresholds(long highWaterMark, int expectedBand)
    {
        AccountNumberAllocationState state = new(highWaterMark + 1, highWaterMark, 1);

        Assert.Equal(expectedBand, AccountNumberAllocationPolicy.DepletionBand(state, 1_000_000));
    }

    [Fact]
    public void A_state_below_its_high_water_mark_is_refused()
    {
        AccountNumberAllocationState state = new(NextValue: 10, HighWaterMark: 11, Version: 1);

        Assert.Throws<InvalidOperationException>(() =>
            AccountNumberAllocationPolicy.Allocate(state, 1, 1_000_000));
    }
}

public sealed class AccountOpeningLifecycleTests
{
    [Fact]
    public void Only_an_approved_application_can_be_opened()
    {
        Assert.Equal(
            AccountApplicationStatus.Open,
            AccountApplicationLifecycle.RecordOpened(AccountApplicationStatus.Approved));

        Assert.Throws<InvalidOperationException>(() =>
            AccountApplicationLifecycle.RecordOpened(AccountApplicationStatus.Eligible));
        Assert.Throws<InvalidOperationException>(() =>
            AccountApplicationLifecycle.RecordOpened(AccountApplicationStatus.Open));
    }

    [Fact]
    public void The_account_number_reveal_permission_is_three_segment_and_stepped_up()
    {
        Assert.Equal("account.number.reveal", AccountPermissions.NumberReveal.Value);
        Assert.Equal("account", AccountPermissions.NumberReveal.Domain);
        Assert.Equal("number", AccountPermissions.NumberReveal.Resource);
        Assert.Equal("reveal", AccountPermissions.NumberReveal.Action);
        Assert.True(AccountPermissions.NumberRevealRequirement.RequiresStepUp);
        Assert.Equal("account.application.approve", AccountPermissions.ApplicationApproveRequirement.Permission.Value);
    }
}

public sealed class AccountOpenedContractTests
{
    [Fact]
    public void The_account_opened_fact_carries_the_masked_number_only()
    {
        AccountOpenedV1 payload = new(
            "institution-1",
            "account-1",
            "********3456",
            "customer-1",
            "product-1",
            "version-1",
            "branch-1",
            "NGN",
            "Open",
            1,
            new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero));

        AccountOpenedV1 restored = EventContract.Deserialize<AccountOpenedV1>(EventContract.Serialize(payload))!;

        Assert.Equal(payload, restored);
        Assert.StartsWith("***", restored.MaskedAccountNumber, StringComparison.Ordinal);
        Assert.Equal("corebanking.core-ledger.account.v1", EventContract.TopicFor("core-ledger", "account", 1));
    }

    [Fact]
    public void The_numbering_request_contracts_carry_no_trusted_field()
    {
        Assert.DoesNotContain("InstitutionId", typeof(CreateAccountNumberingDraftRequestV1).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain("Status", typeof(CreateAccountNumberingDraftRequestV1).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain("ApprovedByActorId", typeof(CreateAccountNumberingDraftRequestV1).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain("AccountNumber", typeof(OpenAccountRequestV1).GetProperties().Select(p => p.Name));
    }
}

public sealed class AccountNumberPersistenceModelTests
{
    private static CoreLedgerDbContext CreateContext()
    {
        AmbientTenantContext tenantContext = new();
        tenantContext.Establish(InstitutionId.From("institution-trusted"));
        DbContextOptions<CoreLedgerDbContext> options = new DbContextOptionsBuilder<CoreLedgerDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model;******")
            .Options;
        return new CoreLedgerDbContext(options, tenantContext);
    }

    [Fact]
    public void An_account_number_and_an_application_produce_at_most_one_account()
    {
        using CoreLedgerDbContext context = CreateContext();
        var account = context.Model.FindEntityType(typeof(AccountRecord))
            ?? throw new InvalidOperationException("Account mapping is missing.");

        Assert.Contains(account.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["InstitutionId", "AccountNumber"]));
        Assert.Contains(account.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["InstitutionId", "AccountApplicationId"]));
        Assert.NotEmpty(account.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_reservation_row_is_concurrency_guarded_and_one_per_scheme_instance()
    {
        using CoreLedgerDbContext context = CreateContext();
        var reservation = context.Model.FindEntityType(typeof(AccountNumberReservationRecord))
            ?? throw new InvalidOperationException("Reservation mapping is missing.");

        Assert.Equal(
            ["InstitutionId", "SchemeVersionId"],
            reservation.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(reservation.FindProperty(nameof(AccountNumberReservationRecord.Version))!.IsConcurrencyToken);
        Assert.NotEmpty(reservation.GetDeclaredQueryFilters());
    }

    [Fact]
    public void A_retired_sequence_is_recorded_once_per_scheme_instance()
    {
        using CoreLedgerDbContext context = CreateContext();
        var retirement = context.Model.FindEntityType(typeof(AccountNumberRetirementRecord))
            ?? throw new InvalidOperationException("Retirement mapping is missing.");

        Assert.Contains(retirement.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(["InstitutionId", "SchemeVersionId", "SequenceValue"]));
    }
}
