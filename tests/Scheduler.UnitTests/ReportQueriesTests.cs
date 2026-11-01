using Scheduler.Infrastructure.Persistence.Queries;

namespace Scheduler.UnitTests;

public sealed class ReportQueriesTests
{
    [Fact]
    public void SecurityEvent_ParsesApplicationIdentity()
    {
        var loggedAt = DateTime.UtcNow;
        const string json = """
            {"message":"A product was created.","properties":{"action":"InventoryProductCreated","user":"admin","application":"Inventory Monitor"}}
            """;

        var result = ReportQueries.SecurityEvent(loggedAt, json);

        Assert.Equal(loggedAt, result.LoggedAtUtc);
        Assert.Equal("Inventory Monitor", result.Application);
        Assert.Equal("admin", result.User);
        Assert.Equal("InventoryProductCreated", result.Action);
        Assert.Equal("A product was created.", result.Message);
    }

    [Fact]
    public void SecurityEvent_LegacyEntryWithoutApplication_IsMarkedUnknown()
    {
        const string json = """{"message":"Legacy event.","properties":{"action":"OldAction","user":"admin"}}""";

        var result = ReportQueries.SecurityEvent(DateTime.UtcNow, json);

        Assert.Equal("Unknown", result.Application);
        Assert.Equal("OldAction", result.Action);
        Assert.Equal("Legacy event.", result.Message);
    }
}
