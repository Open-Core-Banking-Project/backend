using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Diagnostics;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.BuildingBlocks.Infrastructure.Tenancy;
using CoreBanking.IdentityAccess.Infrastructure.Persistence;
using CoreBanking.IdentityAccess.Infrastructure.Persistence.Access;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Messaging;

/// <summary>
/// Outbox, inbox and idempotency behaviour against a real <c>identity_access_db</c>. These tests
/// cover the failure scenarios epic 04 lists: source state and outbox atomicity, a broker failure
/// that retries without recreating the event, exhausted attempts that need a human, a record that
/// is not yet due, duplicate delivery, concurrent consumers and an unsupported schema version.
/// Decision: ADR-0007.
/// </summary>
[Trait("Category", "Database")]
public sealed class OutboxAndInboxTests : IDisposable
{
    private const string ConnectionStringVariableName = "ConnectionStrings__IdentityAccess";
    private const string Consumer = "identity-access.test-consumer";
    private const int SchemaVersion = 1;

    private readonly string _connectionString;
    private readonly InstitutionId _institution = InstitutionId.From("msg-" + Guid.NewGuid().ToString("N")[..16]);

    public OutboxAndInboxTests()
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
    public async Task Source_state_and_the_outbox_record_commit_together_or_not_at_all()
    {
        await using (IdentityAccessDbContext context = CreateContext())
        {
            await using var transaction = await context.BeginTransactionAsync(CancellationToken.None);
            AddRole(context, "role-rolled-back");
            context.OutboxMessages.Add(NewOutbox("event-rolled-back"));
            await context.SaveChangesAsync(CancellationToken.None);
            await transaction.RollbackAsync(CancellationToken.None);
        }

        await using (IdentityAccessDbContext context = CreateContext())
        {
            Assert.Empty(context.Roles.ToList());
            Assert.Empty(context.OutboxMessages.ToList());
        }

        await using (IdentityAccessDbContext context = CreateContext())
        {
            await using var transaction = await context.BeginTransactionAsync(CancellationToken.None);
            AddRole(context, "role-committed");
            context.OutboxMessages.Add(NewOutbox("event-committed"));
            await context.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        await using (IdentityAccessDbContext context = CreateContext())
        {
            Assert.Single(context.Roles.ToList());
            Assert.Single(context.OutboxMessages.ToList());
        }
    }

    [Fact]
    public async Task The_relay_publishes_a_pending_record_once_and_marks_it_published()
    {
        await SeedOutboxAsync(NewOutbox("event-publish"));

        RecordingPublisher publisher = new();
        await using IdentityAccessDbContext context = CreateContext();
        OutboxRelayResult result = await new OutboxRelay(context, publisher, TimeProvider.System).RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, result.Published);

        PublishedMessage message = Assert.Single(publisher.Published);
        Assert.Equal(EventContract.TopicFor("identity-access", "foundation", SchemaVersion), message.Topic);
        Assert.Equal("foundation-1", message.PartitionKey);

        await using (IdentityAccessDbContext verification = CreateContext())
        {
            OutboxRecord record = verification.OutboxMessages.Single();
            Assert.Equal(OutboxStatus.Published, record.Status);
            Assert.NotNull(record.PublishedAt);
        }

        RecordingPublisher secondPublisher = new();
        await using IdentityAccessDbContext secondContext = CreateContext();
        OutboxRelayResult second = await new OutboxRelay(secondContext, secondPublisher, TimeProvider.System).RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, second.Claimed);
        Assert.Empty(secondPublisher.Published);
    }

    [Fact]
    public async Task The_relay_resumes_the_w3c_context_persisted_with_the_outbox_record()
    {
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == CoreBankingActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using Activity parent = new("reference-api-request");
        parent.SetIdFormat(ActivityIdFormat.W3C);
        parent.Start();

        OutboxRecord record = NewOutbox("event-trace-context");
        record.TraceParent = parent.Id;
        record.TraceState = parent.TraceStateString;
        await SeedOutboxAsync(record);

        RecordingPublisher publisher = new();
        await using IdentityAccessDbContext context = CreateContext();
        OutboxRelayResult result = await new OutboxRelay(context, publisher, TimeProvider.System)
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, result.Published);
        Assert.True(publisher.PublishedContext.HasValue);
        Assert.Equal(parent.TraceId, publisher.PublishedContext!.Value.TraceId);
        Assert.Equal(parent.SpanId, publisher.PublishedParentSpanId);
    }

    [Fact]
    public async Task A_broker_failure_schedules_a_retry_without_recreating_the_event()
    {
        await SeedOutboxAsync(NewOutbox("event-retry"));

        RecordingPublisher publisher = new(failure: new EventPublicationException("broker unavailable"));
        await using IdentityAccessDbContext context = CreateContext();
        OutboxRelay relay = new(context, publisher, TimeProvider.System, new OutboxRelayOptions { MaximumAttempts = 5 });

        OutboxRelayResult result = await relay.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Published);
        Assert.Equal(0, result.DeadLettered);

        await using IdentityAccessDbContext verification = CreateContext();
        OutboxRecord record = verification.OutboxMessages.Single();
        Assert.Equal(OutboxStatus.Pending, record.Status);
        Assert.Equal(1, record.Attempts);
        Assert.Null(record.PublishedAt);
        Assert.Equal("event-retry", record.EventId);
    }

    [Fact]
    public async Task Exhausted_attempts_need_a_human_and_raise_a_dead_letter()
    {
        await SeedOutboxAsync(NewOutbox("event-poison"));

        RecordingPublisher publisher = new(failure: new EventPublicationException("broker rejected the message"));
        await using IdentityAccessDbContext context = CreateContext();
        OutboxRelay relay = new(context, publisher, TimeProvider.System, new OutboxRelayOptions { MaximumAttempts = 1 });

        OutboxRelayResult result = await relay.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, result.DeadLettered);

        await using IdentityAccessDbContext verification = CreateContext();
        Assert.Equal(OutboxStatus.ManualRepair, verification.OutboxMessages.Single().Status);

        DeadLetterRecord deadLetter = verification.DeadLetters.Single();
        Assert.Equal(DeadLetterSource.Outbox, deadLetter.Source);
        Assert.Equal("event-poison", deadLetter.EventId);
        Assert.Null(deadLetter.RepairedAt);
    }

    [Fact]
    public async Task A_record_is_not_claimed_before_its_next_attempt_time()
    {
        OutboxRecord record = NewOutbox("event-not-due");
        record.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await SeedOutboxAsync(record);

        RecordingPublisher publisher = new();
        await using IdentityAccessDbContext context = CreateContext();

        OutboxRelayResult result = await new OutboxRelay(context, publisher, TimeProvider.System).RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, result.Claimed);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task A_duplicate_delivery_is_a_no_op_that_returns_the_recorded_outcome()
    {
        IntegrationEventEnvelope envelope = NewEnvelope("event-duplicate");
        int handled = 0;

        Task<string?> Handler(CancellationToken cancellationToken)
        {
            handled++;
            return Task.FromResult<string?>("outcome-for-" + envelope.EventId);
        }

        await using (IdentityAccessDbContext context = CreateContext())
        {
            InboxResult first = await new InboxProcessor(context, TimeProvider.System)
                .ProcessAsync(Consumer, envelope, SchemaVersion, Handler, CancellationToken.None);
            Assert.Equal(InboxResult.Processed, first);
        }

        await using (IdentityAccessDbContext context = CreateContext())
        {
            InboxResult second = await new InboxProcessor(context, TimeProvider.System)
                .ProcessAsync(Consumer, envelope, SchemaVersion, Handler, CancellationToken.None);
            Assert.Equal(InboxResult.Duplicate, second);
        }

        Assert.Equal(1, handled);

        await using IdentityAccessDbContext verification = CreateContext();
        InboxRecord record = verification.InboxMessages.Single();
        Assert.Equal("outcome-for-event-duplicate", record.Outcome);
        Assert.NotNull(record.ProcessedAt);
    }

    [Fact]
    public async Task Concurrent_consumers_converge_on_one_recorded_outcome()
    {
        IntegrationEventEnvelope envelope = NewEnvelope("event-concurrent");
        int handled = 0;

        async Task<InboxResult> ConsumeAsync()
        {
            await using IdentityAccessDbContext context = CreateContext();

            return await new InboxProcessor(context, TimeProvider.System).ProcessAsync(
                Consumer,
                envelope,
                SchemaVersion,
                _ =>
                {
                    Interlocked.Increment(ref handled);
                    return Task.FromResult<string?>("outcome");
                },
                CancellationToken.None);
        }

        InboxResult[] results = await Task.WhenAll(ConsumeAsync(), ConsumeAsync());

        Assert.Contains(InboxResult.Processed, results);
        Assert.DoesNotContain(InboxResult.Deferred, results);

        await using IdentityAccessDbContext verification = CreateContext();
        InboxRecord record = Assert.Single(verification.InboxMessages.ToList());
        Assert.Equal("outcome", record.Outcome);
    }

    [Fact]
    public async Task An_unsupported_schema_version_is_dead_lettered_without_running_the_handler()
    {
        IntegrationEventEnvelope envelope = NewEnvelope("event-future-schema") with { SchemaVersion = 2 };
        bool handled = false;

        await using IdentityAccessDbContext context = CreateContext();

        InboxResult result = await new InboxProcessor(context, TimeProvider.System).ProcessAsync(
            Consumer,
            envelope,
            SchemaVersion,
            _ =>
            {
                handled = true;
                return Task.FromResult<string?>("should not run");
            },
            CancellationToken.None);

        Assert.Equal(InboxResult.Rejected, result);
        Assert.False(handled);

        await using IdentityAccessDbContext verification = CreateContext();
        Assert.Equal("rejected", verification.InboxMessages.Single().Outcome);
        Assert.Contains("unsupported schema version 2", verification.DeadLetters.Single().Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_scoped_idempotency_key_returns_the_original_result_and_rejects_a_different_request()
    {
        IdempotencyKey key = IdempotencyKey.Create(_institution.Value, "foundation.create", "actor-1", "key-1");
        string fingerprint = IdempotencyKey.Fingerprint("{\"amount\":1}");
        int executed = 0;

        await using (IdentityAccessDbContext context = CreateContext())
        {
            IdempotencyOutcome first = await new IdempotencyService(context, TimeProvider.System)
                .ExecuteAsync(key, fingerprint, Command, CancellationToken.None);

            Assert.Equal(IdempotencyOutcomeKind.Executed, first.Kind);
            Assert.Equal("result-1", first.Result);
        }

        await using (IdentityAccessDbContext context = CreateContext())
        {
            IdempotencyOutcome replay = await new IdempotencyService(context, TimeProvider.System)
                .ExecuteAsync(key, fingerprint, Command, CancellationToken.None);

            Assert.Equal(IdempotencyOutcomeKind.Replayed, replay.Kind);
            Assert.Equal("result-1", replay.Result);
        }

        await using (IdentityAccessDbContext context = CreateContext())
        {
            IdempotencyOutcome conflict = await new IdempotencyService(context, TimeProvider.System)
                .ExecuteAsync(key, IdempotencyKey.Fingerprint("{\"amount\":2}"), Command, CancellationToken.None);

            Assert.Equal(IdempotencyOutcomeKind.Conflict, conflict.Kind);
        }

        Assert.Equal(1, executed);

        Task<string?> Command(CancellationToken cancellationToken)
        {
            executed++;
            return Task.FromResult<string?>("result-1");
        }
    }

    [Fact]
    public async Task A_failed_command_releases_the_key_so_a_retry_can_execute()
    {
        IdempotencyKey key = IdempotencyKey.Create(_institution.Value, "foundation.create", "actor-2", "key-2");
        string fingerprint = IdempotencyKey.Fingerprint("{\"amount\":1}");

        await using (IdentityAccessDbContext context = CreateContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new IdempotencyService(context, TimeProvider.System)
                .ExecuteAsync(key, fingerprint, _ => throw new InvalidOperationException("command failed"), CancellationToken.None));
        }

        await using (IdentityAccessDbContext verification = CreateContext())
        {
            Assert.Empty(verification.IdempotencyRecords.ToList());
        }

        await using (IdentityAccessDbContext context = CreateContext())
        {
            IdempotencyOutcome retried = await new IdempotencyService(context, TimeProvider.System)
                .ExecuteAsync(key, fingerprint, _ => Task.FromResult<string?>("result-2"), CancellationToken.None);

            Assert.Equal(IdempotencyOutcomeKind.Executed, retried.Kind);
            Assert.Equal("result-2", retried.Result);
        }
    }

    public void Dispose()
    {
        using IdentityAccessDbContext context = CreateContext();

        context.OutboxMessages.IgnoreQueryFilters().Where(record => record.InstitutionId == _institution.Value).ExecuteDelete();
        context.InboxMessages.IgnoreQueryFilters().Where(record => record.InstitutionId == _institution.Value).ExecuteDelete();
        context.IdempotencyRecords.IgnoreQueryFilters().Where(record => record.InstitutionId == _institution.Value).ExecuteDelete();
        context.DeadLetters.IgnoreQueryFilters().Where(record => record.InstitutionId == _institution.Value).ExecuteDelete();
        context.Roles.ExecuteDelete();
    }

    private IdentityAccessDbContext CreateContext()
    {
        AmbientTenantContext tenant = new();
        tenant.Establish(_institution);

        DbContextOptionsBuilder<IdentityAccessDbContext> options = new();
        options.UseNpgsql(_connectionString);

        return new IdentityAccessDbContext(options.Options, tenant);
    }

    private void AddRole(IdentityAccessDbContext context, string roleId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        context.Roles.Add(new RoleRecord
        {
            InstitutionId = _institution.Value,
            RoleId = roleId,
            RoleCode = roleId,
            RoleName = roleId,
            Version = 1L,
            Status = RecordStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private OutboxRecord NewOutbox(string eventId) => new()
    {
        InstitutionId = _institution.Value,
        MessageId = Guid.NewGuid().ToString("N"),
        EventId = eventId,
        EventType = "foundation_record_created",
        SchemaVersion = SchemaVersion,
        Topic = EventContract.TopicFor("identity-access", "foundation", SchemaVersion),
        BusinessReference = "foundation-1",
        CorrelationId = "correlation-1",
        Producer = "identity-access",
        Envelope = "{}",
        Status = OutboxStatus.Pending,
        NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private IntegrationEventEnvelope NewEnvelope(string eventId) => new()
    {
        EventId = eventId,
        EventType = "foundation_record_created",
        SchemaVersion = SchemaVersion,
        OccurredAt = DateTimeOffset.UtcNow,
        InstitutionId = _institution.Value,
        CorrelationId = "correlation-1",
        Producer = "identity-access",
        BusinessReference = "foundation-1",
        Payload = "{}",
    };

    private async Task SeedOutboxAsync(OutboxRecord record)
    {
        await using IdentityAccessDbContext context = CreateContext();
        context.OutboxMessages.Add(record);
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        private readonly Exception? _failure;

        public RecordingPublisher(Exception? failure = null) => _failure = failure;

        public List<PublishedMessage> Published { get; } = [];

        public ActivityContext? PublishedContext { get; private set; }

        public ActivitySpanId? PublishedParentSpanId { get; private set; }

        public Task PublishAsync(string topic, string partitionKey, string envelope, CancellationToken cancellationToken)
        {
            if (_failure is not null)
            {
                throw _failure;
            }

            if (Activity.Current is Activity activity)
            {
                PublishedContext = activity.Context;
                PublishedParentSpanId = activity.ParentSpanId;
            }

            Published.Add(new PublishedMessage(topic, partitionKey, envelope));
            return Task.CompletedTask;
        }
    }

    private sealed record PublishedMessage(string Topic, string PartitionKey, string Envelope);
}
