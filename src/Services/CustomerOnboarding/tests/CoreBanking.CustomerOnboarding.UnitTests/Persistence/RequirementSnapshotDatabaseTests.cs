using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.CustomerOnboarding.Application.Onboarding;
using CoreBanking.CustomerOnboarding.Application.Requirements;
using CoreBanking.CustomerOnboarding.Domain.Onboarding;
using CoreBanking.CustomerOnboarding.Domain.Parties;
using CoreBanking.CustomerOnboarding.Domain.Requirements;
using CoreBanking.CustomerOnboarding.Infrastructure.Onboarding;
using CoreBanking.CustomerOnboarding.Infrastructure.Persistence;
using CoreBanking.CustomerOnboarding.Infrastructure.Protection;
using CoreBanking.CustomerOnboarding.Infrastructure.Requirements;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.CustomerOnboarding.UnitTests.Persistence;

/// <summary>
/// Requirement snapshots and document reference metadata against a real <c>customer_onboarding_db</c>:
/// submission needs an effective approved pack, the snapshot is immutable and per revision, a rejected
/// document blocks submission and an unscanned document never counts as clean.
/// Decision: P1-E03-D01, invariant 3, invariant 4.
/// </summary>
[Trait("Category", "Database")]
public sealed class RequirementSnapshotDatabaseTests
{
    private const string ConnectionStringVariableName = "ConnectionStrings__CustomerOnboarding";

    private static readonly DateTimeOffset Start = new(2026, 10, 29, 11, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("req-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly InstitutionId _otherInstitution = InstitutionId.From("other-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ActorId _officer = ActorId.From("onboarding-officer-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestTimeProvider _time = new(Start);

    public RequirementSnapshotDatabaseTests()
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
    public async Task Submission_is_refused_without_an_effective_requirement_pack()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);

        OnboardingApplicationView started = await StartAsync(onboarding, "REQ-1");

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => onboarding.SubmitAsync(
                new SubmitOnboardingApplicationCommand(_institution, _officer, "idem-submit-REQ-1", started.ApplicationId, started.Revision, "correlation"),
                CancellationToken.None));

        Assert.Equal("onboarding_requirement_pack_missing", conflict.Code);
        Assert.Equal("draft", (await context.OnboardingApplications.SingleAsync()).Status);
        Assert.Empty(await context.RequirementSnapshots.ToListAsync());
        Assert.Empty(await context.Parties.ToListAsync());
    }

    [Fact]
    public async Task Submission_captures_an_immutable_snapshot_of_the_effective_pack()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        RequirementPackView pack = await PublishPackAsync(context, ["contact_detail", "address_detail"]);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);

        OnboardingApplicationView started = await StartAsync(onboarding, "REQ-2");
        OnboardingApplicationView submitted = await SubmitAsync(onboarding, "REQ-2", started);

        Assert.Equal("submitted", submitted.Status);

        RequirementSnapshotRecord snapshot = await context.RequirementSnapshots.SingleAsync();
        Assert.Equal(pack.PackCode, snapshot.PackCode);
        Assert.Equal(pack.Version, snapshot.PackVersion);
        Assert.Equal(pack.ContentHash, snapshot.ContentHash);
        Assert.Equal(submitted.Revision, snapshot.ApplicationRevision);
        Assert.Equal(_officer.Value, snapshot.CapturedBy);
        Assert.Contains("contact_detail", snapshot.Requirements, StringComparison.Ordinal);
        Assert.Contains("address_detail", snapshot.Requirements, StringComparison.Ordinal);
        Assert.DoesNotContain("ada@example.com", snapshot.Requirements, StringComparison.OrdinalIgnoreCase);

        OnboardingApplicationRecord application = await context.OnboardingApplications.SingleAsync();
        Assert.Equal(snapshot.RequirementSnapshotId, application.RequirementSnapshotId);

        OnboardingApplicationRevisionRecord submittedRevision = await context.OnboardingApplicationRevisions
            .SingleAsync(revision => revision.Reason == "submitted");
        Assert.Equal(snapshot.RequirementSnapshotId, submittedRevision.RequirementSnapshotId);
    }

    [Fact]
    public async Task A_new_pack_version_applies_to_new_submissions_only()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);

        await PublishPackAsync(context, ["contact_detail"]);
        OnboardingApplicationView first = await StartAsync(onboarding, "REQ-3-A");
        await SubmitAsync(onboarding, "REQ-3-A", first);
        RequirementSnapshotRecord firstSnapshot = await context.RequirementSnapshots.SingleAsync();

        RequirementPackView secondPack = await PublishPackAsync(context, ["contact_detail", "address_detail"]);
        OnboardingApplicationView second = await StartAsync(onboarding, "REQ-3-B");
        await SubmitAsync(onboarding, "REQ-3-B", second);

        List<RequirementSnapshotRecord> snapshots = await context.RequirementSnapshots
            .OrderBy(snapshot => snapshot.PackVersion)
            .ToListAsync();

        Assert.Equal(2, snapshots.Count);
        Assert.Equal(1, snapshots[0].PackVersion);
        Assert.Equal(firstSnapshot.ContentHash, snapshots[0].ContentHash);
        Assert.DoesNotContain("address_detail", snapshots[0].Requirements, StringComparison.Ordinal);

        Assert.Equal(2, snapshots[1].PackVersion);
        Assert.Equal(secondPack.ContentHash, snapshots[1].ContentHash);
        Assert.Contains("address_detail", snapshots[1].Requirements, StringComparison.Ordinal);

        RequirementPackRecord firstVersion = await context.RequirementPacks.SingleAsync(record => record.Version == 1);
        Assert.Equal("superseded", firstVersion.Status);
        Assert.NotNull(firstVersion.SupersededAt);
    }

    [Fact]
    public async Task A_rejected_document_blocks_submission_while_an_unscanned_document_does_not()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        await PublishPackAsync(context, ["contact_detail"]);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);

        OnboardingApplicationView blocked = await StartAsync(onboarding, "REQ-4-A");
        RequirementService rejecting = new(context, new RejectingDocumentQuarantine(), _time);
        DocumentReferenceView rejected = await rejecting.RegisterDocumentAsync(
            DocumentCommand(blocked.ApplicationId),
            CancellationToken.None);
        Assert.Equal("rejected", rejected.ScanStatus);

        OnboardingApplicationConflictException conflict = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => onboarding.SubmitAsync(
                new SubmitOnboardingApplicationCommand(_institution, _officer, "idem-submit-REQ-4-A", blocked.ApplicationId, blocked.Revision, "correlation"),
                CancellationToken.None));
        Assert.Equal("onboarding_document_scan_rejected", conflict.Code);

        OnboardingApplicationView allowed = await StartAsync(onboarding, "REQ-4-B");
        RequirementService pending = new(context, new PendingScanDocumentQuarantine(), _time);
        DocumentReferenceView pendingScan = await pending.RegisterDocumentAsync(
            DocumentCommand(allowed.ApplicationId),
            CancellationToken.None);
        Assert.Equal("pending_scan", pendingScan.ScanStatus);

        OnboardingApplicationView submitted = await SubmitAsync(onboarding, "REQ-4-B", allowed);
        Assert.Equal("submitted", submitted.Status);
    }

    [Fact]
    public async Task A_document_reference_requires_a_lowercase_hash_and_a_positive_length()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);
        OnboardingApplicationView started = await StartAsync(onboarding, "REQ-5");
        RequirementService requirements = new(context, new PendingScanDocumentQuarantine(), _time);

        OnboardingApplicationConflictException hash = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => requirements.RegisterDocumentAsync(
                DocumentCommand(started.ApplicationId) with { ContentHash = "NOT-A-HASH" },
                CancellationToken.None));
        Assert.Equal("document_content_hash_invalid", hash.Code);

        OnboardingApplicationConflictException length = await Assert.ThrowsAsync<OnboardingApplicationConflictException>(
            () => requirements.RegisterDocumentAsync(
                DocumentCommand(started.ApplicationId) with { ContentLength = 0 },
                CancellationToken.None));
        Assert.Equal("document_content_length_invalid", length.Code);
    }

    [Fact]
    public async Task Only_one_snapshot_can_exist_per_application_revision()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        await PublishPackAsync(context, ["contact_detail"]);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);

        OnboardingApplicationView started = await StartAsync(onboarding, "REQ-6");
        await SubmitAsync(onboarding, "REQ-6", started);
        RequirementSnapshotRecord existing = await context.RequirementSnapshots.SingleAsync();

        context.RequirementSnapshots.Add(new RequirementSnapshotRecord
        {
            InstitutionId = _institution.Value,
            RequirementSnapshotId = Guid.NewGuid().ToString("N"),
            ApplicationId = existing.ApplicationId,
            ApplicationRevision = existing.ApplicationRevision,
            PackCode = existing.PackCode,
            PackVersion = existing.PackVersion,
            ContentHash = existing.ContentHash,
            Requirements = existing.Requirements,
            CapturedBy = _officer.Value,
            CapturedAt = Start,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Another_institution_cannot_read_the_snapshot()
    {
        await using CustomerOnboardingDbContext context = CreateContext(_institution);
        await PublishPackAsync(context, ["contact_detail"]);
        OnboardingApplicationService onboarding = CreateOnboardingService(context);
        OnboardingApplicationView started = await StartAsync(onboarding, "REQ-7");
        await SubmitAsync(onboarding, "REQ-7", started);

        RequirementSnapshotRecord snapshot = await context.RequirementSnapshots.SingleAsync();
        RequirementService own = new(context, new PendingScanDocumentQuarantine(), _time);
        RequirementSnapshotView view = await own.GetSnapshotAsync(
            new GetRequirementSnapshotQuery(_institution, _officer, snapshot.RequirementSnapshotId),
            CancellationToken.None);
        Assert.Equal(snapshot.ContentHash, view.ContentHash);
        Assert.Equal("contact_detail", Assert.Single(view.Requirements).Code);

        await using CustomerOnboardingDbContext otherContext = CreateContext(_otherInstitution);
        RequirementService other = new(otherContext, new PendingScanDocumentQuarantine(), _time);

        await Assert.ThrowsAsync<RequirementSnapshotNotFoundException>(
            () => other.GetSnapshotAsync(
                new GetRequirementSnapshotQuery(_otherInstitution, _officer, snapshot.RequirementSnapshotId),
                CancellationToken.None));
    }

    private RegisterDocumentCommand DocumentCommand(string applicationId) =>
        new(
            _institution,
            _officer,
            applicationId,
            "contact_detail",
            "application/pdf",
            2048,
            new string('a', 64),
            "storage://documents/synthetic-reference",
            "correlation");

    private async Task<RequirementPackView> PublishPackAsync(CustomerOnboardingDbContext context, string[] requirementCodes)
    {
        RequirementService requirements = new(context, new PendingScanDocumentQuarantine(), _time);
        return await requirements.PublishPackAsync(
            new PublishRequirementPackCommand(
                _institution,
                _officer,
                "phase1-test",
                [.. requirementCodes.Select(code => OnboardingRequirement.Create(code, code.Replace('_', ' '), true, ["document"]))],
                "correlation"),
            CancellationToken.None);
    }

    private async Task<OnboardingApplicationView> StartAsync(OnboardingApplicationService onboarding, string reference) =>
        await onboarding.StartAsync(
            new StartIndividualApplicationCommand(
                _institution,
                _officer,
                "idem-start-" + reference,
                reference,
                "branch-1",
                CompleteCapture(),
                "correlation"),
            CancellationToken.None);

    private async Task<OnboardingApplicationView> SubmitAsync(
        OnboardingApplicationService onboarding,
        string reference,
        OnboardingApplicationView started) =>
        await onboarding.SubmitAsync(
            new SubmitOnboardingApplicationCommand(
                _institution,
                _officer,
                "idem-submit-" + reference,
                started.ApplicationId,
                started.Revision,
                "correlation"),
            CancellationToken.None);

    private static OnboardingCapture CompleteCapture() =>
        new(
            PersonName.Create("Ada", null, "Lovelace"),
            BirthDetails.Create(new DateOnly(1990, 5, 12), "GB", DateOnly.FromDateTime(Start.UtcDateTime)),
            [ContactPoint.Create(ContactPointType.Email, "ada@example.com")],
            [Address.Create(AddressType.Residential, "12 Analytical Engine Way", null, "London", null, "GB")]);

    private CustomerOnboardingDbContext CreateContext(InstitutionId institution)
    {
        AmbientTenantContext tenant = new();
        tenant.Establish(institution);

        return new CustomerOnboardingDbContext(
            new DbContextOptionsBuilder<CustomerOnboardingDbContext>().UseNpgsql(_connectionString).Options,
            tenant);
    }

    private OnboardingApplicationService CreateOnboardingService(CustomerOnboardingDbContext context) =>
        new(
            context,
            new AesGcmProtectedValueProtector(CreateProtectionOptions()),
            new IdempotencyService(context, _time),
            _time);

    private static ProtectedValueProtectionOptions CreateProtectionOptions() =>
        ProtectedValueProtectionOptions.Create(
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)(32 + index)).ToArray()),
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)(96 + index)).ToArray()));

    private sealed class RejectingDocumentQuarantine : IDocumentQuarantine
    {
        public Task<DocumentScanResult> SubmitAsync(DocumentScanRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new DocumentScanResult(DocumentScanStatus.Rejected, "scan-rejected"));
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
