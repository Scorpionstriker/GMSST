using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduler.Domain.Entities;

namespace Scheduler.Infrastructure.Persistence.Configurations;

/// <summary>task_queue table (Phase 4, Table 5.5 and Section 5.5).</summary>
internal sealed class QueueEntryConfiguration : IEntityTypeConfiguration<QueueEntry>
{
    public void Configure(EntityTypeBuilder<QueueEntry> b)
    {
        b.ToTable("task_queue", t =>
        {
            t.HasCheckConstraint("ck_task_queue_priority", "priority BETWEEN 1 AND 10");
            t.HasCheckConstraint("ck_task_queue_attempt", "attempt >= 0");
            t.HasCheckConstraint("ck_task_queue_lease", "claimed_until IS NULL OR claimed_until > claimed_at");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("queue_id").ValueGeneratedNever();
        b.Property(x => x.TaskId).HasColumnName("task_id");
        b.Property(x => x.ScheduleId).HasColumnName("schedule_id");
        b.Property(x => x.TriggerType).IsEnumText(b, "trigger_type", 20);
        b.Property(x => x.Priority).HasColumnName("priority");
        b.Property(x => x.ScheduledTimeUtc).HasColumnName("scheduled_time");
        b.Property(x => x.EnqueuedAtUtc).HasColumnName("enqueued_at").HasDefaultValueSql("now()");
        b.Property(x => x.Status).IsEnumText(b, "queue_status", 20);
        b.Property(x => x.ClaimedBy).HasColumnName("claimed_by");
        b.Property(x => x.ClaimedAtUtc).HasColumnName("claimed_at");
        b.Property(x => x.ClaimedUntilUtc).HasColumnName("claimed_until");
        b.Property(x => x.Attempt).HasColumnName("attempt").HasDefaultValue(0);
        b.Property(x => x.IsManualRun).HasColumnName("is_manual_run").HasDefaultValue(false);
        b.Property(x => x.EventName).HasColumnName("event_name").HasMaxLength(100);
        b.Property(x => x.EventSource).HasColumnName("event_source").HasMaxLength(100);
        b.Property(x => x.EventDataJson).HasColumnName("event_data").HasMaxLength(64 * 1024);

        b.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_task_queue_task");
        b.HasOne<Schedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_task_queue_schedule");
        b.HasOne<WorkerThread>().WithMany().HasForeignKey(x => x.ClaimedBy).OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_task_queue_worker");

        b.HasIndex(x => new { x.Priority, x.ScheduledTimeUtc, x.EnqueuedAtUtc }).HasDatabaseName("ix_queue_claim").HasFilter("queue_status = 'Waiting'");
        b.HasIndex(x => x.ClaimedUntilUtc).HasDatabaseName("ix_queue_lease").HasFilter("queue_status = 'Claimed'");
        b.HasIndex(x => x.TaskId).HasDatabaseName("ix_queue_task");
    }
}

/// <summary>worker_thread table (Phase 4, Table 5.10).</summary>
internal sealed class WorkerThreadConfiguration : IEntityTypeConfiguration<WorkerThread>
{
    public void Configure(EntityTypeBuilder<WorkerThread> b)
    {
        b.ToTable("worker_thread");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("thread_id").ValueGeneratedNever();
        b.Property(x => x.HostName).HasColumnName("host_name").HasMaxLength(WorkerThread.HostNameMaxLength).IsRequired();
        b.Property(x => x.ThreadPoolName).HasColumnName("thread_pool_name").HasMaxLength(50).IsRequired();
        b.Property(x => x.Status).IsEnumText(b, "status", 10);
        b.Property(x => x.CurrentExecutionId).HasColumnName("current_execution_id");
        b.Property(x => x.LastHeartbeatUtc).HasColumnName("last_heartbeat");
        b.Property(x => x.StartedAtUtc).HasColumnName("started_at");
        b.HasIndex(x => x.HostName).HasDatabaseName("ix_worker_host");
    }
}
