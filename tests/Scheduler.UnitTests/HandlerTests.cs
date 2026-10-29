using System.Text.Json;
using DemoApps.MaintenanceAgent.Handlers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Scheduler.Domain.Enums;
using Scheduler.Engine.Handlers;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.UnitTests;

/// <summary>A handler that is misconfigured must fail with a clear message, never report success.</summary>
public class HandlerTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "message": "" }""")]
    [InlineData("""{ "message": "hi", "level": "Loud" }""")]
    public async Task LogEntry_FailsForAnIncompletePayload(string payload)
    {
        var result = await new LogEntryHandler().ExecuteAsync(new FakeContext(payload));

        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task LogEntry_SucceedsForAValidPayload()
    {
        var result = await new LogEntryHandler().ExecuteAsync(new FakeContext("""{ "message": "Hello", "level": "Warning" }"""));

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "url": "not a url" }""")]
    [InlineData("""{ "url": "ftp://localhost/file" }""")]
    [InlineData("""{ "url": "http://example.com/" }""")]
    [InlineData("""{ "url": "http://10.0.0.5/" }""")]
    [InlineData("""{ "url": "http://localhost/", "method": "TRACE" }""")]
    public async Task LocalHttp_RefusesMissingOrOutsideAddresses(string payload)
    {
        var result = await new LocalHttpRequestHandler().ExecuteAsync(new FakeContext(payload));

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("http://localhost:5000/health", true)]
    [InlineData("http://127.0.0.1/", true)]
    [InlineData("http://[::1]/", true)]
    [InlineData("http://localhost.example.com/", false)]
    [InlineData("http://192.168.1.10/", false)]
    public void LocalHttp_OnlyLoopbackIsAllowed(string url, bool allowed)
    {
        Assert.Equal(allowed, LocalHttpRequestHandler.IsLoopback(new Uri(url)));
    }

    [Fact]
    public void TempFileCleanup_OnlyWorksInsideAllowedFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "GMSS");
        string[] roots = [root];

        Assert.True(TempFileCleanupHandler.IsInside(root, roots));
        Assert.True(TempFileCleanupHandler.IsInside(Path.Combine(root, "reports"), roots));
        Assert.False(TempFileCleanupHandler.IsInside(root + "-other", roots));
        Assert.False(TempFileCleanupHandler.IsInside(Path.Combine(root, "..", "elsewhere"), roots));
        Assert.False(TempFileCleanupHandler.IsInside(Path.GetTempPath(), roots));
    }

    private sealed class FakeContext(string payload) : ITaskContext
    {
        public Guid TaskId { get; } = Guid.NewGuid();
        public Guid ExecutionId { get; } = Guid.NewGuid();
        public string TaskName => "Test";
        public int RetryNumber => 0;
        public TriggerType TriggerType => TriggerType.Manual;
        public DateTime ScheduledTimeUtc { get; } = DateTime.UtcNow;
        public JsonElement Payload { get; } = JsonDocument.Parse(payload).RootElement.Clone();
        public ILogger Logger => NullLogger.Instance;
        public CancellationToken CancellationToken => CancellationToken.None;
        public IServiceProvider Services => throw new NotSupportedException();
    }
}
