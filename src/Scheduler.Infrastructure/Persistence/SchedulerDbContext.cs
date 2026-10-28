using Microsoft.EntityFrameworkCore;

namespace Scheduler.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the scheduler schema of the local PostgreSQL database (Phase 4, Section 5).
/// Demonstration modules keep their own data in a separate schema (FR-78).
/// </summary>
public class SchedulerDbContext(DbContextOptions<SchedulerDbContext> options) : DbContext(options)
{
    public const string Schema = "scheduler";

    // Add a DbSet for each entity as it is implemented, for example:
    // public DbSet<ScheduledTask> Tasks => Set<ScheduledTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SchedulerDbContext).Assembly);
    }
}
