using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DemoApps.MaintenanceAgent.Services;
using DemoApps.MaintenanceAgentConnector;
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

        Assert.True(TempFileCleaner.IsInside(root, roots));
        Assert.True(TempFileCleaner.IsInside(Path.Combine(root, "reports"), roots));
        Assert.False(TempFileCleaner.IsInside(root + "-other", roots));
        Assert.False(TempFileCleaner.IsInside(Path.Combine(root, "..", "elsewhere"), roots));
        Assert.False(TempFileCleaner.IsInside(Path.GetTempPath(), roots));
    }

    [Fact]
    public void LocalHttp_ReadsEnvHeaderValuesWhenTheTaskRuns()
    {
        Environment.SetEnvironmentVariable("GSST_TEST_HEADER_KEY", "secret-value");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1/");
            var error = LocalHttpRequestHandler.AddHeaders(
                new FakeContext("""{ "headers": { "X-GSST-Key": "env:GSST_TEST_HEADER_KEY", "X-Plain": "text" } }"""), request);

            Assert.Null(error);
            Assert.Equal("secret-value", request.Headers.GetValues("X-GSST-Key").Single());
            Assert.Equal("text", request.Headers.GetValues("X-Plain").Single());
        }
        finally
        {
            Environment.SetEnvironmentVariable("GSST_TEST_HEADER_KEY", null);
        }
    }

    [Fact]
    public void LocalHttp_FailsWhenAnEnvHeaderVariableIsMissing()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1/");
        var error = LocalHttpRequestHandler.AddHeaders(new FakeContext("""{ "headers": { "X-GSST-Key": "env:GSST_TEST_NOT_SET_ANYWHERE" } }"""), request);

        Assert.Equal("The environment variable GSST_TEST_NOT_SET_ANYWHERE for header 'X-GSST-Key' is not set on this computer.", error);
    }

    [Fact]
    public async Task LocalHttp_UsesTheNotRunningMessageWhenNothingListens()
    {
        // Port 9 (discard) is not served on a development computer, so the connection is refused.
        var result = await new LocalHttpRequestHandler().ExecuteAsync(new FakeContext(
            """{ "url": "http://127.0.0.1:9/health", "notRunningMessage": "The Maintenance Agent is not running on port 9." }"""));

        Assert.False(result.IsSuccess);
        Assert.Equal("The Maintenance Agent is not running on port 9.", result.Message);
    }

    [Fact]
    public async Task LocalHttp_IncludesOptedInFailureResponseInErrorMessage()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        const string responseBody = """{"healthy":false,"message":"Inventory database: unhealthy"}""";
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while ((await reader.ReadLineAsync()) is { Length: > 0 })
            {
            }

            var response = $"HTTP/1.1 503 Service Unavailable\r\nContent-Length: {Encoding.UTF8.GetByteCount(responseBody)}\r\nConnection: close\r\n\r\n{responseBody}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        });
        var result = await new LocalHttpRequestHandler().ExecuteAsync(new FakeContext(
            $$"""{"url":"http://127.0.0.1:{{port}}/health","expectedStatus":200,"includeResponseInError":true}"""));
        await server;

        Assert.False(result.IsSuccess);
        Assert.Contains(responseBody, result.Message);
        var output = JsonDocument.Parse(JsonSerializer.Serialize(result.Output)).RootElement;
        Assert.Equal(responseBody, output.GetProperty("response").GetString());
    }

    [Fact]
    public void AgentKey_IsRequiredAndCompared()
    {
        Assert.True(new AgentKeyFilter("abc").IsValid("abc"));
        Assert.False(new AgentKeyFilter("abc").IsValid("abd"));
        Assert.False(new AgentKeyFilter("abc").IsValid(null));
        Assert.False(new AgentKeyFilter(null).IsValid(""));
        Assert.False(new AgentKeyFilter(null).IsValid("anything"));
    }

    [Fact]
    public void Connector_TaskPayloadKeepsTheKeyOutOfTheDatabase()
    {
        var payload = JsonDocument.Parse(MaintenanceAgentConnectorModule.BuildPayload(new AgentClient(), "/health", "GET")).RootElement;
        var healthPayload = JsonDocument.Parse(MaintenanceAgentConnectorModule.BuildPayload(
            new AgentClient(), "/health", "GET", includeResponseInError: true)).RootElement;

        Assert.Equal("http://127.0.0.1:5087/health", payload.GetProperty("url").GetString());
        Assert.Equal("env:GSST_AGENT_KEY", payload.GetProperty("headers").GetProperty("X-GSST-Key").GetString());
        Assert.Equal("The Maintenance Agent is not running on port 5087.", payload.GetProperty("notRunningMessage").GetString());
        Assert.False(payload.TryGetProperty("body", out _));
        Assert.True(healthPayload.GetProperty("includeResponseInError").GetBoolean());
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
