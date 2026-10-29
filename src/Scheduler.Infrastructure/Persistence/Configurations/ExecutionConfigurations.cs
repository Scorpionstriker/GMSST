using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduler.Domain.Entities;

namespace Scheduler.Infrastructure.Persistence.Configurations;

/// <summary>task_execution table (Phase 4, Table 5.6).</summary>
internal sealed class TaskExecutionConfiguration : IEntityTypeConfiguration<TaskExecution>
{
    public void Configure(EntityTypeBuilder<TaskExecution> b)
    {
        b.ToTable("task_execution", t =>
        {
            t.HasCheckConstraint("ck_task_execution_end", "end_time IS NULL OR end_time >= start_time");
            t.HasCheckConstraint("ck_task_execution_duration", "duration_ms IS NULL OR duration_ms >= 0");
            t.HasCheckConstraint("ck_task_execution_retry_count", "retry_count >= 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("execution_id").ValueGeneratedNever();
        b.Property(x => x.TaskId).HasColumnName("task_id");
        b.Property(x => x.ScheduleId).HasColumnName("schedule_id");
        b.Property(x => x.QueueId).HasColumnName("queue_id");
        b.Property(x => x.ThreadId).HasColumnName("thread_id");
        b.Property(x => x.TriggerType).IsEnumText(b, "trigger_type", 20);
        b.Property(x => x.ScheduledTimeUtc).HasColumnName("scheduled_time");
        b.Property(x => x.StartTimeUtc).HasColumnName("start_time");
        b.Property(x => x.EndTimeUtc).HasColumnName("end_time");
        b.Property(x => x.DurationMs).HasColumnName("duration_ms");
        b.Property(x => x.Status).IsEnumText(b, "execution_status", 20);
        b.Property(x => x.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
        b.Property(x => x.Output).HasColumnName("output").HasColumnType("jsonb");
        b.Ignore(x => x.StartDelayMs);

        b.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_task_execution_task");
        b.HasOne<Schedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_task_execution_schedule");
        // Nullable with SET NULL so the seven-day queue purge keeps the execution history (see docs/Design_Deviations.md).
        b.HasOne<QueueEntry>().WithOne().HasForeignKey<TaskExecution>(x => x.QueueId).OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_task_execution_queue");
        b.HasOne<WorkerThread>().WithMany().HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_task_execution_worker");

        b.HasIndex(x => x.QueueId).IsUnique().HasDatabaseName("ux_task_execution_queue");
        b.HasIndex(x => new { x.TaskId, x.StartTimeUtc }).IsDescending(false, true).HasDatabaseName("ix_execution_task_time");
        b.HasIndex(x => new { x.Status, x.StartTimeUtc }).HasDatabaseName("ix_execution_status_time");
        b.HasIndex(x => x.StartTimeUtc).HasDatabaseName("ix_execution_start_time");
    }
}

/// <summary>error_log table (Phase 4, Table 5.7).</summary>
internal sealed class ErrorLogConfiguration : IEntityTypeConfiguration<ErrorLogEntry>
{
    public void Configure(EntityTypeBuilder<ErrorLogEntry> b)
    {
        b.ToTable("error_log");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("error_id").ValueGeneratedNever();
        b.Property(x => x.ExecutionId).HasColumnName("execution_id");
        b.Property(x => x.ErrorType).HasColumnName("error_type").HasMaxLength(ErrorLogEntry.ErrorTypeMaxLength).IsRequired();
        b.Property(x => x.ErrorMessage).HasColumnName("error_message").IsRequired();
        b.Property(x => x.StackTrace).HasColumnName("stack_trace").HasColumnType("jsonb");
        b.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at").HasDefaultValueSql("now()");
        b.HasOne(x => x.Execution).WithMany().HasForeignKey(x => x.ExecutionId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_error_log_execution");
        b.HasIndex(x => x.ExecutionId).HasDatabaseName("ix_error_execution");
        b.HasIndex(x => x.OccurredAtUtc).HasDatabaseName("ix_error_time");
    }
}

/// <summary>execution_log table (Phase 4, Table 5.8).</summary>
internal sealed class ExecutionLogConfiguration : IEntityTypeConfiguration<ExecutionLogEntry>
{
    public void Configure(EntityTypeBuilder<ExecutionLogEntry> b)
    {
        b.ToTable("execution_log");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("log_id").ValueGeneratedNever();
        b.Property(x => x.ExecutionId).HasColumnName("execution_id");
        b.Property(x => x.ProviderId).HasColumnName("provider_id");
        b.Property(x => x.Level).IsEnumText(b, "log_level", 10);
        b.Property(x => x.Source).HasColumnName("source").HasMaxLength(ExecutionLogEntry.SourceMaxLength).IsRequired();
        b.Property(x => x.Message).HasColumnName("message").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.LoggedAtUtc).HasColumnName("logged_at").HasDefaultValueSql("now()");
        b.HasOne<TaskExecution>().WithMany().HasForeignKey(x => x.ExecutionId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_execution_log_execution");
        b.HasOne<LoggingProviderSetting>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_execution_log_provider");
        b.HasIndex(x => x.LoggedAtUtc).HasDatabaseName("ix_log_time");
        b.HasIndex(x => x.ExecutionId).HasDatabaseName("ix_log_execution");
    }
}
