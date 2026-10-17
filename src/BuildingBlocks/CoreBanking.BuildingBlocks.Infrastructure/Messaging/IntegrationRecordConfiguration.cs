using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Technical mappings for the four integration tables. Every service applies these to its own
/// database context and owns the resulting migration and retention policy; no domain type is
/// referenced here. Decision: ADR-0007.
/// </summary>
public static class IntegrationRecordConfiguration
{
    public static void ApplyIntegrationRecords(this ModelBuilder modelBuilder)
    {
        System.ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new OutboxRecordConfiguration());
        modelBuilder.ApplyConfiguration(new InboxRecordConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());
        modelBuilder.ApplyConfiguration(new DeadLetterRecordConfiguration());
    }

    private sealed class OutboxRecordConfiguration : IEntityTypeConfiguration<OutboxRecord>
    {
        public void Configure(EntityTypeBuilder<OutboxRecord> builder)
        {
            builder.ToTable("outbox_messages", "integration");
            builder.HasKey(record => new { record.InstitutionId, record.MessageId });

            builder.Property(record => record.InstitutionId).HasColumnName("institution_id");
            builder.Property(record => record.MessageId).HasColumnName("message_id");
            builder.Property(record => record.EventId).HasColumnName("event_id");
            builder.Property(record => record.EventType).HasColumnName("event_type");
            builder.Property(record => record.SchemaVersion).HasColumnName("schema_version");
            builder.Property(record => record.Topic).HasColumnName("topic");
            builder.Property(record => record.BusinessReference).HasColumnName("business_reference");
            builder.Property(record => record.CorrelationId).HasColumnName("correlation_id");
            builder.Property(record => record.CausationId).HasColumnName("causation_id");
            builder.Property(record => record.Producer).HasColumnName("producer");
            builder.Property(record => record.Envelope).HasColumnName("envelope");
            builder.Property(record => record.Status).HasColumnName("status").HasConversion<string>();
            builder.Property(record => record.Attempts).HasColumnName("attempts");
            builder.Property(record => record.NextAttemptAt).HasColumnName("next_attempt_at");
            builder.Property(record => record.LastError).HasColumnName("last_error");
            builder.Property(record => record.CreatedAt).HasColumnName("created_at");
            builder.Property(record => record.PublishedAt).HasColumnName("published_at");

            builder.HasIndex(record => new { record.InstitutionId, record.EventId }).IsUnique();
            builder.HasIndex(record => new { record.Status, record.NextAttemptAt });
        }
    }

    private sealed class InboxRecordConfiguration : IEntityTypeConfiguration<InboxRecord>
    {
        public void Configure(EntityTypeBuilder<InboxRecord> builder)
        {
            builder.ToTable("inbox_messages", "integration");
            builder.HasKey(record => new { record.InstitutionId, record.Consumer, record.EventId });

            builder.Property(record => record.InstitutionId).HasColumnName("institution_id");
            builder.Property(record => record.Consumer).HasColumnName("consumer");
            builder.Property(record => record.EventId).HasColumnName("event_id");
            builder.Property(record => record.EventType).HasColumnName("event_type");
            builder.Property(record => record.SchemaVersion).HasColumnName("schema_version");
            builder.Property(record => record.Outcome).HasColumnName("outcome");
            builder.Property(record => record.Attempts).HasColumnName("attempts");
            builder.Property(record => record.LastError).HasColumnName("last_error");
            builder.Property(record => record.ReceivedAt).HasColumnName("received_at");
            builder.Property(record => record.ProcessedAt).HasColumnName("processed_at");
        }
    }

    private sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
    {
        public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
        {
            builder.ToTable("idempotency_records", "integration");
            builder.HasKey(record => new { record.InstitutionId, record.Operation, record.Initiator, record.IdempotencyKey });

            builder.Property(record => record.InstitutionId).HasColumnName("institution_id");
            builder.Property(record => record.Operation).HasColumnName("operation");
            builder.Property(record => record.Initiator).HasColumnName("initiator");
            builder.Property(record => record.IdempotencyKey).HasColumnName("idempotency_key");
            builder.Property(record => record.RequestFingerprint).HasColumnName("request_fingerprint");
            builder.Property(record => record.Status).HasColumnName("status").HasConversion<string>();
            builder.Property(record => record.Result).HasColumnName("result");
            builder.Property(record => record.CreatedAt).HasColumnName("created_at");
            builder.Property(record => record.CompletedAt).HasColumnName("completed_at");

            builder.HasIndex(record => new { record.Status, record.CreatedAt });
        }
    }

    private sealed class DeadLetterRecordConfiguration : IEntityTypeConfiguration<DeadLetterRecord>
    {
        public void Configure(EntityTypeBuilder<DeadLetterRecord> builder)
        {
            builder.ToTable("dead_letters", "integration");
            builder.HasKey(record => new { record.InstitutionId, record.DeadLetterId });

            builder.Property(record => record.InstitutionId).HasColumnName("institution_id");
            builder.Property(record => record.DeadLetterId).HasColumnName("dead_letter_id");
            builder.Property(record => record.Source).HasColumnName("source").HasConversion<string>();
            builder.Property(record => record.EventId).HasColumnName("event_id");
            builder.Property(record => record.EventType).HasColumnName("event_type");
            builder.Property(record => record.Payload).HasColumnName("payload");
            builder.Property(record => record.Reason).HasColumnName("reason");
            builder.Property(record => record.Attempts).HasColumnName("attempts");
            builder.Property(record => record.CreatedAt).HasColumnName("created_at");
            builder.Property(record => record.RepairedAt).HasColumnName("repaired_at");

            builder.HasIndex(record => new { record.RepairedAt, record.CreatedAt });
        }
    }
}
