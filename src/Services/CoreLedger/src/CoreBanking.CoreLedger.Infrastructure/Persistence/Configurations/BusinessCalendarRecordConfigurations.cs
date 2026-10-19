using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoreBanking.CoreLedger.Infrastructure.Persistence.Configurations;

internal sealed class BusinessCalendarRecordConfiguration : IEntityTypeConfiguration<BusinessCalendarRecord>
{
    public void Configure(EntityTypeBuilder<BusinessCalendarRecord> builder)
    {
        builder.ToTable("business_calendars", "institution");
        builder.HasKey(x => new { x.InstitutionId, x.CalendarId });
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.CalendarId).HasColumnName("calendar_id").HasMaxLength(128);
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(256);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}

internal sealed class CalendarVersionRecordConfiguration : IEntityTypeConfiguration<CalendarVersionRecord>
{
    public void Configure(EntityTypeBuilder<CalendarVersionRecord> builder)
    {
        builder.ToTable("calendar_versions", "institution", table =>
        {
            table.HasCheckConstraint("ck_calendar_versions_status", "status IN ('draft', 'published')");
            table.HasCheckConstraint("ck_calendar_versions_number", "version_number > 0");
            table.HasCheckConstraint("ck_calendar_versions_period", "effective_to >= effective_from");
        });
        builder.HasKey(x => new { x.InstitutionId, x.VersionId });
        builder.HasIndex(x => new { x.InstitutionId, x.CalendarId, x.VersionNumber }).IsUnique();
        builder.HasIndex(x => new { x.InstitutionId, x.CalendarId, x.EffectiveFrom, x.EffectiveTo });
        builder.HasOne<BusinessCalendarRecord>().WithMany().HasForeignKey(x => new { x.InstitutionId, x.CalendarId }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.VersionId).HasColumnName("version_id").HasMaxLength(128);
        builder.Property(x => x.CalendarId).HasColumnName("calendar_id").HasMaxLength(128);
        builder.Property(x => x.VersionNumber).HasColumnName("version_number");
        builder.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(128);
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to");
        builder.Property(x => x.InitialBusinessDate).HasColumnName("initial_business_date");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.CreatedByActorId).HasColumnName("created_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ApprovedByActorId).HasColumnName("approved_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ApprovalReference).HasColumnName("approval_reference").HasMaxLength(128);
        builder.Property(x => x.ApprovalReason).HasColumnName("approval_reason").HasMaxLength(512);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");
    }
}

internal sealed class CalendarDayRecordConfiguration : IEntityTypeConfiguration<CalendarDayRecord>
{
    public void Configure(EntityTypeBuilder<CalendarDayRecord> builder)
    {
        builder.ToTable("calendar_days", "institution", table =>
            table.HasCheckConstraint("ck_calendar_days_classification", "classification IN ('working_day', 'holiday', 'non_working_day')"));
        builder.HasKey(x => new { x.InstitutionId, x.VersionId, x.Date });
        builder.HasOne<CalendarVersionRecord>().WithMany().HasForeignKey(x => new { x.InstitutionId, x.VersionId }).OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.VersionId).HasColumnName("version_id").HasMaxLength(128);
        builder.Property(x => x.Date).HasColumnName("calendar_date");
        builder.Property(x => x.Classification).HasColumnName("classification").HasMaxLength(32);
        builder.Property(x => x.IsProcessingDay).HasColumnName("is_processing_day");
    }
}

internal sealed class BusinessDateControlRecordConfiguration : IEntityTypeConfiguration<BusinessDateControlRecord>
{
    public void Configure(EntityTypeBuilder<BusinessDateControlRecord> builder)
    {
        builder.ToTable("business_date_controls", "institution");
        builder.HasKey(x => x.InstitutionId);
        builder.HasOne<CalendarVersionRecord>().WithMany().HasForeignKey(x => new { x.InstitutionId, x.CalendarVersionId }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.CurrentBusinessDate).HasColumnName("current_business_date");
        builder.Property(x => x.CalendarVersionId).HasColumnName("calendar_version_id").HasMaxLength(128);
        builder.Property(x => x.LastAdvanceRequestId).HasColumnName("last_advance_request_id").HasMaxLength(128);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
    }
}

internal sealed class BusinessDateAdvanceRequestRecordConfiguration : IEntityTypeConfiguration<BusinessDateAdvanceRequestRecord>
{
    public void Configure(EntityTypeBuilder<BusinessDateAdvanceRequestRecord> builder)
    {
        builder.ToTable("business_date_advance_requests", "institution", table =>
        {
            table.HasCheckConstraint("ck_business_date_requests_status", "status IN ('pending_approval', 'executed')");
            table.HasCheckConstraint("ck_business_date_requests_forward", "new_business_date > expected_current_date");
        });
        builder.HasKey(x => new { x.InstitutionId, x.RequestId });
        builder.HasIndex(x => new { x.InstitutionId, x.Status, x.RequestedAt });
        builder.HasOne<CalendarVersionRecord>().WithMany().HasForeignKey(x => new { x.InstitutionId, x.CalendarVersionId }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(x => x.ExpectedCurrentDate).HasColumnName("expected_current_date");
        builder.Property(x => x.NewBusinessDate).HasColumnName("new_business_date");
        builder.Property(x => x.CalendarVersionId).HasColumnName("calendar_version_id").HasMaxLength(128);
        builder.Property(x => x.ProcessingCheckpointId).HasColumnName("processing_checkpoint_id").HasMaxLength(128);
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(512);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.RequestedByActorId).HasColumnName("requested_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ApprovedByActorId).HasColumnName("approved_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ApprovalReference).HasColumnName("approval_reference").HasMaxLength(128);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.RequestedAt).HasColumnName("requested_at");
        builder.Property(x => x.ApprovedAt).HasColumnName("approved_at");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
    }
}

internal sealed class BusinessDateHistoryRecordConfiguration : IEntityTypeConfiguration<BusinessDateHistoryRecord>
{
    public void Configure(EntityTypeBuilder<BusinessDateHistoryRecord> builder)
    {
        builder.ToTable("business_date_history", "institution", table =>
        {
            table.HasCheckConstraint("ck_business_date_history_forward", "business_date > previous_business_date");
            table.HasCheckConstraint("ck_business_date_history_skipped", "skipped_non_processing_days >= 0");
        });
        builder.HasKey(x => new { x.InstitutionId, x.HistoryId });
        builder.HasIndex(x => new { x.InstitutionId, x.BusinessDate }).IsUnique();
        builder.HasIndex(x => new { x.InstitutionId, x.RequestId }).IsUnique();
        builder.HasOne<CalendarVersionRecord>().WithMany().HasForeignKey(x => new { x.InstitutionId, x.CalendarVersionId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessDateAdvanceRequestRecord>().WithMany().HasForeignKey(x => new { x.InstitutionId, x.RequestId }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128);
        builder.Property(x => x.HistoryId).HasColumnName("history_id").HasMaxLength(128);
        builder.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(128);
        builder.Property(x => x.PreviousBusinessDate).HasColumnName("previous_business_date");
        builder.Property(x => x.BusinessDate).HasColumnName("business_date");
        builder.Property(x => x.SkippedNonProcessingDays).HasColumnName("skipped_non_processing_days");
        builder.Property(x => x.CalendarVersionId).HasColumnName("calendar_version_id").HasMaxLength(128);
        builder.Property(x => x.ProcessingCheckpointId).HasColumnName("processing_checkpoint_id").HasMaxLength(128);
        builder.Property(x => x.RequestedByActorId).HasColumnName("requested_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ApprovedByActorId).HasColumnName("approved_by_actor_id").HasMaxLength(128);
        builder.Property(x => x.ApprovalReference).HasColumnName("approval_reference").HasMaxLength(128);
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(512);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.AdvancedAt).HasColumnName("advanced_at");
    }
}
