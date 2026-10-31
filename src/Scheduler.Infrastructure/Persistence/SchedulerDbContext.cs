using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Entities;

namespace Scheduler.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the scheduler schema of the local PostgreSQL database: the twelve tables of
/// Phase 4, Section 5 and the engine_instance table of the single-engine rule (WP-1). Demonstration modules keep
/// their own data in their own databases (FR-78).
/// </summary>
public class SchedulerDbContext(DbContextOptions<SchedulerDbContext> options) : DbContext(options)
{
    public const string Schema = "scheduler";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<ScheduledTask> Tasks => Set<ScheduledTask>();
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<QueueEntry> TaskQueue => Set<QueueEntry>();
    public DbSet<TaskExecution> TaskExecutions => Set<TaskExecution>();
    public DbSet<ErrorLogEntry> ErrorLogs => Set<ErrorLogEntry>();
    public DbSet<ExecutionLogEntry> ExecutionLogs => Set<ExecutionLogEntry>();
    public DbSet<ConfigurationSetting> Configuration => Set<ConfigurationSetting>();
    public DbSet<WorkerThread> WorkerThreads => Set<WorkerThread>();
    public DbSet<LoggingProviderSetting> LoggingProviders => Set<LoggingProviderSetting>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();
    public DbSet<EngineInstance> EngineInstances => Set<EngineInstance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SchedulerDbContext).Assembly);
    }
}
