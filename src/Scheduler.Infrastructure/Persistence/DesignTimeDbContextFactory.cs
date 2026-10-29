using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Scheduler.Infrastructure.Persistence;

/// <summary>Used only by the dotnet ef tools to create migrations; it reads the password from GMSS_DB_PASSWORD.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SchedulerDbContext>
{
    public SchedulerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SchedulerDbContext>();
        DependencyInjection.ConfigureDbContext(options, DatabaseConnection.FromEnvironment(new DatabaseOptions()));
        return new SchedulerDbContext(options.Options);
    }
}
