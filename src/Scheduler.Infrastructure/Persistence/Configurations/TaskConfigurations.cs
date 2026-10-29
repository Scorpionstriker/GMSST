using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.Infrastructure.Persistence.Configurations;

/// <summary>task table (Phase 4, Table 5.2).</summary>
internal sealed class ScheduledTaskConfiguration : IEntityTypeConfiguration<ScheduledTask>
{
    public void Configure(EntityTypeBuilder<ScheduledTask> b)
    {
        b.ToTable("task", t =>
        {
            t.HasCheckConstraint("ck_task_priority", "priority BETWEEN 1 AND 10");
            t.HasCheckConstraint("ck_task_max_retries", "max_retries IS NULL OR (max_retries BETWEEN 0 AND 10)");
            t.HasCheckConstraint("ck_task_timeout_seconds", "timeout_seconds > 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("task_id").ValueGeneratedNever();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(ScheduledTask.NameMaxLength).IsRequired();
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.TaskType).HasColumnName("task_type").HasMaxLength(ScheduledTask.TaskTypeMaxLength).IsRequired();
        b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        b.Property(x => x.Priority).HasColumnName("priority").HasDefaultValue(ScheduledTask.DefaultPriority).HasSentinel(0);
        b.Property(x => x.Status).IsEnumText(b, "status", 20);
        b.Property(x => x.MaxRetries).HasColumnName("max_retries");
        b.Property(x => x.TimeoutSeconds).HasColumnName("timeout_seconds").HasDefaultValue(ScheduledTask.DefaultTimeoutSeconds).HasSentinel(0);
        b.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        b.Property(x => x.CreatedById).HasColumnName("created_by");
        b.Property(x => x.UpdatedById).HasColumnName("updated_by");
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at").HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at");

        // Optimistic concurrency on PostgreSQL's xmin system column (Phase 4, Section 5.7).
        b.Property(x => x.Version).IsRowVersion();

        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_task_created_by");
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UpdatedById).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_task_updated_by");

        b.HasMany(x => x.Schedules).WithOne(s => s.Task).HasForeignKey(s => s.TaskId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_schedule_task");
        b.Navigation(x => x.Schedules).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Dependencies).WithOne().HasForeignKey(d => d.TaskId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_task_dependency_task");
        b.Navigation(x => x.Dependencies).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.CurrentSchedule);
        // ux_task_name (lower(name) WHERE NOT is_deleted) is created in the migration, because EF Core cannot express it.
        b.HasIndex(x => x.Status).HasDatabaseName("ix_task_status");
    }
}

/// <summary>schedule table (Phase 4, Table 5.3).</summary>
internal sealed class ScheduleConfiguration : IEntityTypeConfiguration<Schedule>
{
    public void Configure(EntityTypeBuilder<Schedule> b)
    {
        b.ToTable("schedule", t =>
        {
            t.HasCheckConstraint("ck_schedule_cron_required", "schedule_type <> 'Cron' OR cron_expression IS NOT NULL");
            t.HasCheckConstraint("ck_schedule_interval_required", "schedule_type <> 'Interval' OR interval_seconds IS NOT NULL");
            t.HasCheckConstraint("ck_schedule_interval_positive", "interval_seconds IS NULL OR interval_seconds > 0");
            t.HasCheckConstraint("ck_schedule_window", "end_time IS NULL OR end_time > start_time");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("schedule_id").ValueGeneratedNever();
        b.Property(x => x.TaskId).HasColumnName("task_id");
        b.Property(x => x.Type).IsEnumText(b, "schedule_type", 10);
        b.Property(x => x.CronExpression).HasColumnName("cron_expression").HasMaxLength(Schedule.CronMaxLength);
        b.Property(x => x.IntervalSeconds).HasColumnName("interval_seconds");
        b.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(Schedule.TimeZoneMaxLength).HasDefaultValue(Schedule.DefaultTimeZone)
            .IsRequired();
        b.Property(x => x.MisfirePolicy).IsEnumText(b, "misfire_policy", 20);
        b.Property(x => x.StartTimeUtc).HasColumnName("start_time");
        b.Property(x => x.EndTimeUtc).HasColumnName("end_time");
        b.Property(x => x.NextRunTimeUtc).HasColumnName("next_run_time");
        b.Property(x => x.LastRunTimeUtc).HasColumnName("last_run_time");
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).HasSentinel(true);
        b.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        b.Property(x => x.CreatedById).HasColumnName("created_by");
        b.Property(x => x.UpdatedById).HasColumnName("updated_by");
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at").HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at");
        b.Ignore(x => x.Version);

        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_schedule_created_by");
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UpdatedById).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_schedule_updated_by");

        b.HasIndex(x => x.NextRunTimeUtc).HasDatabaseName("ix_schedule_next_run").HasFilter("is_active AND NOT is_deleted");
    }
}

/// <summary>task_dependency table (Phase 4, Table 5.4).</summary>
internal sealed class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> b)
    {
        b.ToTable("task_dependency", t => t.HasCheckConstraint("ck_task_dependency_not_self", "depends_on_task_id <> task_id"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("dependency_id").ValueGeneratedNever();
        b.Property(x => x.TaskId).HasColumnName("task_id");
        b.Property(x => x.DependsOnTaskId).HasColumnName("depends_on_task_id");
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at").HasDefaultValueSql("now()");
        b.HasOne<ScheduledTask>().WithMany().HasForeignKey(x => x.DependsOnTaskId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_task_dependency_depends_on");
        b.HasIndex(x => new { x.TaskId, x.DependsOnTaskId }).IsUnique().HasDatabaseName("ux_task_dependency");
        b.HasIndex(x => x.DependsOnTaskId).HasDatabaseName("ix_task_dependency_depends_on");
    }
}
