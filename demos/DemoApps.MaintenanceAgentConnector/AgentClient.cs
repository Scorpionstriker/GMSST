using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace DemoApps.MaintenanceAgentConnector;

/// <summary>The last health check and clean-up as GET /status reports them.</summary>
public sealed record AgentHealth(DateTime CheckedAtUtc, bool Healthy, string Message);

public sealed record AgentCleanup(DateTime RanAtUtc, bool Succeeded, string Folder, int Deleted, int Skipped, long Bytes, bool DryRun, string Message);

public sealed record AgentStatus(string Name, string Version, DateTime StartedAtUtc, long UptimeSeconds, AgentHealth? LastHealth,
    AgentCleanup? LastCleanup, string? DatabaseError);

/// <summary>The answer to a status request: running with a status, running but refusing the key, or not running.</summary>
public sealed record AgentStatusResult(bool Running, AgentStatus? Status, string Message);

/// <summary>
/// Calls the Maintenance Agent's GET /status on the loopback address. The URL comes from "MaintenanceAgent:Url"
/// (default http://127.0.0.1:5087) and the key from the GSST_AGENT_KEY environment variable; neither is stored in
/// the database.
/// </summary>
public sealed class AgentClient
{
    public const string DefaultUrl = "http://127.0.0.1:5087";
    public const string KeyVariable = "GSST_AGENT_KEY";
    public const string KeyHeader = "X-GSST-Key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly HttpClient Client = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public AgentClient(IConfiguration? configuration = null)
    {
        var url = configuration?["MaintenanceAgent:Url"];
        BaseUrl = (string.IsNullOrWhiteSpace(url) ? DefaultUrl : url).TrimEnd('/');
        DatabaseName = configuration?["MaintenanceAgent:Database"] is { Length: > 0 } name ? name : "maintenance_demo";
    }

    public string BaseUrl { get; }

    /// <summary>The agent's own database, which a restore must not replace while the agent runs.</summary>
    public string DatabaseName { get; }

    public string Port => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ? uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) : "5087";

    /// <summary>The error text of the scheduled runs when the agent does not answer.</summary>
    public string NotRunningMessage => $"The Maintenance Agent is not running on port {Port}.";

    public async Task<AgentStatusResult> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Get, "/status");
        try
        {
            using var response = await Client.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new AgentStatusResult(true, null,
                    $"The Maintenance Agent is running but refused the key. Make sure {KeyVariable} has the same value for both applications, then restart them.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new AgentStatusResult(true, null, $"The Maintenance Agent answered with status {(int)response.StatusCode}.");
            }

            var status = await response.Content.ReadFromJsonAsync<AgentStatus>(Json, cancellationToken);
            return new AgentStatusResult(true, status, "The Maintenance Agent is running.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new AgentStatusResult(false, null, NotRunningMessage);
        }
    }

    /// <summary>
    /// Asks the agent to shut down (POST /shutdown). Returns true when it accepted; false when it is not running, refused
    /// the key, or is an older build without the stop command.
    /// </summary>
    public async Task<bool> ShutdownAsync(CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Post, "/shutdown");
        try
        {
            using var response = await Client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, BaseUrl + path);
        if (Environment.GetEnvironmentVariable(KeyVariable) is { Length: > 0 } key)
        {
            request.Headers.Add(KeyHeader, key);
        }

        return request;
    }
}
