using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Domain;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;
using CoreBanking.CustomerOnboarding.Infrastructure.Onboarding;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using CoreBanking.CustomerOnboarding.Infrastructure.Protection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Persistence;

/// <summary>
/// Individual onboarding capture against a real <c>customer_onboarding_db</c>, as the application
/// principal. The suite needs the local infrastructure stack, is excluded from the default run and is
/// executed by <c>scripts/db-verify.ps1</c> after the migrations are applied.
/// Decision: CUS-UC-01, CUS-028, ADR-0004.
/// </summary>
[Trait("Category", "Database")]
public sealed class OnboardingCaptureDatabaseTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("onboarding-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _actor = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public OnboardingCaptureDatabaseTests()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringVariableName} to the application principal connection string, or run scripts/db-verify.ps1.");
        }

        _connectionString = configured;
    }

    [Fact]
    public async Task Starting_an_application_records_revision_one_with_a_payload_hash_and_no_plaintext()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService service = CreateService(context);

        OnboardingApplicationView view = await service.StartAsync(StartCommand("APP-1", CompleteCapture()), CancellationToken.None);

        Assert.Equal("draft", view.Status);
        Assert.Equal(1, view.Revision);
        Assert.Equal(1L, view.ConcurrencyVersion);
        Assert.Empty(view.MissingFields);
        Assert.False(view.Replayed);

        OnboardingApplicationRecord record = await context.OnboardingApplications.SingleAsync();
        Assert.Equal(64, record.CurrentPayloadHash.Length);
        Assert.DoesNotContain("ada@example.com", record.CurrentCapture, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2348012345678", record.CurrentCapture, StringComparison.Ordinal);

        OnboardingApplicationRevisionRecord revision = await context.OnboardingApplicationRevisions.SingleAsync();
        Assert.Equal(1, revision.Revision);
        Assert.Equal("captured", revision.Reason);
        Assert.Equal(record.CurrentPayloadHash, revision.PayloadHash);
        Assert.Equal(_actor.Value, revision.RecordedBy);
    }

    [Fact]
    public async Task A_revision_appends_a_new_row_and_keeps_the_earlier_evidence()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService service = CreateService(context);

        OnboardingApplicationView started = await service.StartAsync(StartCommand("APP-2", CompleteCapture()), CancellationToken.None);
        string firstHash = (await context.OnboardingApplicationRevisions.SingleAsync()).PayloadHash;

        OnboardingApplicationView revised = await service.UpdateAsync(
            new UpdateIndividualApplicationCommand(
                _institution,
                _actor,
                "idem-update-1",
                started.ApplicationId,
                started.ConcurrencyVersion,
                IncompleteCapture(),
                "correlation-2"),
            CancellationToken.None);

        Assert.Equal(2, revised.Revision);
        Assert.Equal(2L, revised.ConcurrencyVersion);
        Assert.Equal(["birthDate", "contacts", "addresses"], revised.MissingFields);

        List<OnboardingApplicationRevisionRecord> revisions = await context.OnboardingApplicationRevisions
            .OrderBy(record => record.Revision)
            .ToListAsync();

        Assert.Equal(2, revisions.Count);
        Assert.Equal(firstHash, revisions[0].PayloadHash);
        Assert.NotEqual(firstHash, revisions[1].PayloadHash);
    }

    [Fact]
    public async Task A_stale_update_is_rejected_with_a_revision_conflict()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService service = CreateService(context);

        OnboardingApplicationView started = await service.StartAsync(StartCommand("APP-3", CompleteCapture()), CancellationToken.None);
        UpdateIndividualApplicationCommand update = new(
            _institution,
            _actor,
            "idem-update-2",
            started.ApplicationId,
            started.ConcurrencyVersion,
            IncompleteCapture(),
            "correlation-3");

        await service.UpdateAsync(update, CancellationToken.None);

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => service.UpdateAsync(update with { IdempotencyKey = "idem-update-3" }, CancellationToken.None));

        Assert.Equal("onboarding_revision_conflict", conflict.Code);
    }

    [Fact]
    public async Task Submitting_creates_a_pending_party_from_the_protected_snapshot()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService service = CreateService(context);

        OnboardingApplicationView started = await service.StartAsync(StartCommand("APP-4", CompleteCapture()), CancellationToken.None);
        OnboardingApplicationView submitted = await service.SubmitAsync(
            new SubmitOnboardingApplicationCommand(
                _institution,
                _actor,
                "idem-submit-1",
                started.ApplicationId,
                started.Revision,
                "correlation-4"),
            CancellationToken.None);

        Assert.Equal("submitted", submitted.Status);
        Assert.NotNull(submitted.PartyId);
        Assert.Equal(2, submitted.Revision);

        PartyRecord party = await context.Parties
            .Include(record => record.Individual)
            .Include(record => record.Names)
            .Include(record => record.Contacts)
            .Include(record => record.Addresses)
            .SingleAsync(record => record.PartyId == submitted.PartyId);

        Assert.Equal("pending", party.Status);
        Assert.Equal("individual", party.PartyType);
        Assert.Equal(1990, party.Individual!.BirthYear);
        Assert.StartsWith("v1.", party.Individual.BirthDateToken, StringComparison.Ordinal);

        PartyContactRecord contact = Assert.Single(party.Contacts);
        Assert.Equal("email", contact.ContactType);
        Assert.StartsWith("v1.", contact.ValueToken, StringComparison.Ordinal);
        Assert.DoesNotContain("ada", contact.MaskedValue, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(64, contact.LookupToken.Length);

        PartyNameRecord name = Assert.Single(party.Names);
        Assert.Equal("ADA LOVELACE", name.Normalised);

        PartyAddressRecord address = Assert.Single(party.Addresses);
        Assert.Equal("GB", address.CountryCode);
        Assert.DoesNotContain("Analytical", address.MaskedLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_incomplete_capture_cannot_be_submitted()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService service = CreateService(context);

        OnboardingApplicationView started = await service.StartAsync(
            StartCommand("APP-5", OnboardingCapture.Empty),
            CancellationToken.None);

        OnboardingRuleException exception = await Assert.ThrowsAsync<OnboardingRuleException>(
            () => service.SubmitAsync(
                new SubmitOnboardingApplicationCommand(_institution, _actor, "idem-submit-2", started.ApplicationId, started.Revision, "correlation-5"),
                CancellationToken.None));

        Assert.Equal("onboarding_required_field_missing", exception.Code);

        OnboardingApplicationRecord record = await context.OnboardingApplications.SingleAsync();
        Assert.Equal("draft", record.Status);
        Assert.Null(record.PartyId);
        Assert.Empty(context.Parties);
    }

    [Fact]
    public async Task A_submitted_application_cannot_be_revised()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService service = CreateService(context);

        OnboardingApplicationView started = await service.StartAsync(StartCommand("APP-6", CompleteCapture()), CancellationToken.None);
        OnboardingApplicationView submitted = await service.SubmitAsync(
            new SubmitOnboardingApplicationCommand(_institution, _actor, "idem-submit-3", started.ApplicationId, started.Revision, "correlation-6"),
            CancellationToken.None);

        OnboardingRuleException exception = await Assert.ThrowsAsync<OnboardingRuleException>(
            () => service.UpdateAsync(
                new UpdateIndividualApplicationCommand(
                    _institution,
                    _actor,
                    "idem-update-4",
                    submitted.ApplicationId,
                    submitted.ConcurrencyVersion,
                    IncompleteCapture(),
                    "correlation-7"),
                CancellationToken.None));

        Assert.Equal("onboarding_revision_conflict", exception.Code);
    }

    [Fact]
    public async Task A_withdrawn_application_keeps_its_revisions_and_the_party_it_created()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService service = CreateService(context);

        OnboardingApplicationView started = await service.StartAsync(StartCommand("APP-7", CompleteCapture()), CancellationToken.None);
        OnboardingApplicationView submitted = await service.SubmitAsync(
            new SubmitOnboardingApplicationCommand(_institution, _actor, "idem-submit-4", started.ApplicationId, started.Revision, "correlation-8"),
            CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(10));
        OnboardingApplicationView withdrawn = await service.WithdrawAsync(
            new WithdrawOnboardingApplicationCommand(
                _institution,
                _actor,
                "idem-withdraw-1",
                submitted.ApplicationId,
                submitted.ConcurrencyVersion,
                "correlation-9"),
            CancellationToken.None);

        Assert.Equal("withdrawn", withdrawn.Status);
        Assert.NotNull(withdrawn.SubmittedAt);
        Assert.Single(await context.Parties.ToListAsync());
        Assert.Equal(3, await context.OnboardingApplicationRevisions.CountAsync());
    }

    [Fact]
    public async Task A_stored_revision_cannot_be_updated_or_deleted()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService service = CreateService(context);

        OnboardingApplicationView started = await service.StartAsync(StartCommand("APP-8", CompleteCapture()), CancellationToken.None);

        PostgresException update = await Assert.ThrowsAsync<PostgresException>(
            () => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE onboarding.application_revisions SET status = 'draft' WHERE institution_id = {_institution.Value} AND application_id = {started.ApplicationId}"));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, update.SqlState);

        PostgresException delete = await Assert.ThrowsAsync<PostgresException>(
            () => context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM onboarding.application_revisions WHERE institution_id = {_institution.Value} AND application_id = {started.ApplicationId}"));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, delete.SqlState);
    }

    [Fact]
    public async Task The_same_request_replays_without_creating_a_second_application()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService service = CreateService(context);

        OnboardingApplicationView first = await service.StartAsync(StartCommand("APP-9", CompleteCapture()), CancellationToken.None);
        OnboardingApplicationView replay = await service.StartAsync(StartCommand("APP-9", CompleteCapture()), CancellationToken.None);

        Assert.Equal(first.ApplicationId, replay.ApplicationId);
        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(1, await context.OnboardingApplications.CountAsync());

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => service.StartAsync(StartCommand("APP-9", IncompleteCapture()), CancellationToken.None));

        Assert.Equal("idempotency_key_reused_with_different_request", conflict.Code);
    }

    [Fact]
    public async Task Another_institution_cannot_read_the_application()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService service = CreateService(context);

        OnboardingApplicationView started = await service.StartAsync(StartCommand("APP-10", CompleteCapture()), CancellationToken.None);

        await using CustomerOnboardingDbContext otherContext = CreateContext(_otherInstitution);
        OnboardingApplicationService otherService = CreateService(otherContext);

        await Assert.ThrowsAsync<OnboardingApplicationNotFoundException>(
            () => otherService.GetAsync(new GetOnboardingApplicationQuery(_otherInstitution, _actor, started.ApplicationId), CancellationToken.None));
    }

    [Fact]
    public async Task The_application_reference_is_unique_within_an_institution()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService service = CreateService(context);

        await service.StartAsync(StartCommand("APP-11", CompleteCapture()), CancellationToken.None);

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => service.StartAsync(
                new StartIndividualApplicationCommand(_institution, _actor, "idem-start-second", "APP-11", "branch-1", CompleteCapture(), "correlation-12"),
                CancellationToken.None));

        Assert.Equal("onboarding_reference_conflict", conflict.Code);
    }

    private StartIndividualApplicationCommand StartCommand(string reference, OnboardingCapture capture) =>
        new(_institution, _actor, "idem-start-" + reference, reference, "branch-1", capture, "correlation-1");

    private static OnboardingCapture CompleteCapture() =>
        new(
            PersonName.Create("Ada", null, "Lovelace"),
            BirthDetails.Create(new DateOnly(1990, 5, 12), "GB", DateOnly.FromDateTime(Start.UtcDateTime)),
            [ContactPoint.Create(ContactPointType.Email, "ada@example.com")],
            [Address.Create(AddressType.Residential, "12 Analytical Engine Way", null, "London", null, "GB")]);

    private static OnboardingCapture IncompleteCapture() =>
        new(PersonName.Create("Ada", null, "Lovelace"), null, [], []);

    private CustomerOnboardingDbContext CreateContext(InstitutionId institution)
    {
        AmbientTenantContext tenant = new();
        tenant.Establish(institution);

        return new CustomerOnboardingDbContext(
            new DbContextOptionsBuilder<CustomerOnboardingDbContext>().UseNpgsql(_connectionString).Options,
            tenant);
    }

    private OnboardingApplicationService CreateService(CustomerOnboardingDbContext context) =>
        new(
            context,
            new AesGcmProtectedValueProtector(CreateProtectionOptions()),
            new IdempotencyService(context, _time),
            _time);

    private static ProtectedValueProtectionOptions CreateProtectionOptions() =>
        ProtectedValueProtectionOptions.Create(
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)(32 + index)).ToArray()),
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)(96 + index)).ToArray()));

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
