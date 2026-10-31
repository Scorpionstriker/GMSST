using System.Net;
using System.Reflection;
using DemoApps.MaintenanceAgent;
using DemoApps.MaintenanceAgent.Data;
using DemoApps.MaintenanceAgent.Services;
using Npgsql;

// Demonstration app 2 (FR-79, WP-1): a separate application with its own database maintenance_demo and no scheduling
// engine. The scheduler (the Admin application) manages it by calling this API with Local HTTP Request tasks. It listens
// only on the loopback addresses, so nothing outside this computer can reach it, and every request must carry the
// shared key from GSST_AGENT_KEY. Runs on Windows, macOS and Linux. Stop it with Ctrl+C, or launch and stop it from the
// scheduler's Modules page.
var builder = WebApplication.CreateBuilder(args);
var agent = builder.Configuration.GetSection(AgentOptions.SectionName).Get<AgentOptions>() ?? new AgentOptions();
var database = builder.Configuration.GetSection(AgentDatabaseOptions.SectionName).Get<AgentDatabaseOptions>() ?? new AgentDatabaseOptions();

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Listen(IPAddress.Loopback, agent.Port);
    if (System.Net.Sockets.Socket.OSSupportsIPv6)
    {
        // Some computers have IPv6 switched off; the agent then listens on 127.0.0.1 only.
        kestrel.Listen(IPAddress.IPv6Loopback, agent.Port);
    }
});

builder.Services.Configure<MaintenanceOptions>(builder.Configuration.GetSection(MaintenanceOptions.SectionName));
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(new NpgsqlConnectionStringBuilder
{
    Host = database.Host,
    Port = database.Port,
    Database = database.Name,
    Username = database.Username,
    Password = Environment.GetEnvironmentVariable(AgentDatabaseOptions.PasswordVariable),
    ApplicationName = "GSST Maintenance Agent"
}.ConnectionString).Build());
builder.Services.AddSingleton<MaintenanceStore>();
builder.Services.AddSingleton<HealthChecker>();
builder.Services.AddSingleton<TempFileCleaner>();

var app = builder.Build();
var startedAtUtc = DateTime.UtcNow;
var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
var keyFilter = new AgentKeyFilter(Environment.GetEnvironmentVariable(AgentOptions.KeyVariable));
if (!keyFilter.HasKey)
{
    app.Logger.LogWarning("{Variable} is not set, so every request is refused. Run database\\setup_windows.ps1 or set the variable.",
        AgentOptions.KeyVariable);
}

Directory.CreateDirectory(MaintenanceOptions.DefaultRoot);
try
{
    await app.Services.GetRequiredService<MaintenanceStore>().EnsureSchemaAsync();
}
catch (Exception ex) when (MaintenanceStore.IsDatabaseError(ex))
{
    // The agent still starts; GET /health reports the database problem until it is fixed.
    app.Logger.LogError(ex, "The database {Database} could not be prepared.", database.Name);
}

var api = app.MapGroup("").AddEndpointFilter(keyFilter);

api.MapGet("/health", async (HealthChecker checker, CancellationToken cancellationToken) =>
{
    var report = await checker.CheckAsync(cancellationToken);
    return Results.Json(report, statusCode: report.Healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

api.MapPost("/cleanup", async (HttpRequest request, TempFileCleaner cleaner, CancellationToken cancellationToken) =>
{
    var body = request.ContentLength is > 0 ? await request.ReadFromJsonAsync<CleanupRequest>(cancellationToken) : null;
    var result = await cleaner.RunAsync(body ?? new CleanupRequest(), cancellationToken);
    return Results.Json(result, statusCode: result.Succeeded ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
});

api.MapGet("/status", async (MaintenanceStore store, CancellationToken cancellationToken) =>
{
    HealthRecord? health = null;
    CleanupRecord? cleanup = null;
    string? databaseError = null;
    try
    {
        health = await store.GetLastHealthCheckAsync(cancellationToken);
        cleanup = await store.GetLastCleanupRunAsync(cancellationToken);
    }
    catch (Exception ex) when (MaintenanceStore.IsDatabaseError(ex))
    {
        databaseError = ex.Message;
    }

    return Results.Json(new
    {
        name = "GSST Maintenance Agent",
        version,
        startedAtUtc,
        uptimeSeconds = (long)(DateTime.UtcNow - startedAtUtc).TotalSeconds,
        lastHealth = health,
        lastCleanup = cleanup,
        databaseError
    });
});

// Lets the scheduler's Stop button end the agent; it answers first, then shuts down as Ctrl+C would.
api.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
{
    _ = Task.Delay(TimeSpan.FromMilliseconds(200)).ContinueWith(_ => lifetime.StopApplication(), TaskScheduler.Default);
    return Results.Json(new { stopping = true });
});

app.Run();
