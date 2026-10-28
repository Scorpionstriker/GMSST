using DemoApps.MaintenanceAgent.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scheduler.Application;
using Scheduler.Engine;
using Scheduler.Extensibility.Contracts;
using Scheduler.Infrastructure;
using Scheduler.Infrastructure.Persistence;

// Embeds the scheduler core in a plain console host (FR-42, FR-79). It shares the Admin application's
// database, so its tasks appear there, and it only claims the task types registered below (FR-25).
var builder = Host.CreateApplicationBuilder(args);

var database = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();

builder.Services
    .AddSchedulerApplication()
    .AddSchedulerInfrastructure(database)
    .AddSchedulerEngine();

builder.Services.AddSingleton<ITaskHandler, HealthCheckHandler>();
builder.Services.AddSingleton<ITaskHandler, TempFileCleanupHandler>();

builder.Services.AddHostedService(sp => sp.GetRequiredService<SchedulerEngine>());

await builder.Build().RunAsync();
