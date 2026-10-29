using DemoApps.MaintenanceAgent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scheduler.Application;
using Scheduler.Engine;
using Scheduler.Engine.Hosting;
using Scheduler.Infrastructure;

// Demonstration app 2 (FR-79): the scheduler core embedded in a headless console host. It shares the Admin
// application's database, so its tasks appear there, and it only claims the task types it registers (FR-25).
// Runs on Windows, macOS and Linux. Stop it with Ctrl+C: running tasks get ShutdownGraceSeconds to finish (FR-34).
var builder = Host.CreateApplicationBuilder(args);
var engine = builder.Configuration.GetSection(EngineOptions.SectionName).Get<EngineOptions>() ?? new EngineOptions();

builder.Services.Configure<MaintenanceOptions>(builder.Configuration.GetSection(MaintenanceOptions.SectionName));
builder.Services
    .AddSchedulerApplication()
    .AddSchedulerInfrastructure(builder.Configuration)
    .AddSchedulerEngine(engine)
    .AddSchedulerModules(new MaintenanceModule());
builder.Services.AddHostedService<EngineHostedService>();
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(60));

await builder.Build().RunAsync();
