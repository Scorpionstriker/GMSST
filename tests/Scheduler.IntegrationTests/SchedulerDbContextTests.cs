using Microsoft.EntityFrameworkCore;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.IntegrationTests;

public class SchedulerDbContextTests
{
    [Fact]
    public void Model_UsesTheSchedulerSchema()
    {
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseNpgsql(new DatabaseOptions().BuildConnectionString(password: null))
            .Options;

        using var context = new SchedulerDbContext(options);

        Assert.Equal(SchedulerDbContext.Schema, context.Model.GetDefaultSchema());
    }

    [Fact]
    public void ConnectionString_PointsAtLocalhostWithoutAPassword()
    {
        var connectionString = new DatabaseOptions().BuildConnectionString(password: null);

        Assert.Contains("Host=localhost", connectionString);
        Assert.DoesNotContain("Password", connectionString);
    }
}
