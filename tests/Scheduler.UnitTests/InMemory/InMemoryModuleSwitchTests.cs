using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Modules;
using Scheduler.Application.Tasks;
using Scheduler.Domain.Enums;
using Scheduler.Engine;
using Scheduler.Engine.Hosting;
using Scheduler.Extensibility.Contracts;
using Scheduler.Infrastructure.Configuration;

namespace Scheduler.UnitTests.InMemory;

/// <summary>Switching modules on and off on the Modules page, and launching and stopping their applications.</summary>
public sealed class InMemoryModuleSwitchTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gsst-modules-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EveryModuleIsOnByDefault_AndASwitchedOffModulesTaskTypesAreNotRun()
    {
        var module = new TestModule("Test module");
        await using var host = new InMemoryHost(services => services.AddSchedulerModules(module));
        var switches = host.Get<IModuleSwitches>();
        var registry = host.Get<ITaskTypeRegistry>();

        Assert.True(switches.IsEnabled("Test module"));
        Assert.Contains(CountingHandler.Type, registry.TaskTypeNames);

        await switches.SetEnabledAsync("Test module", false);

        Assert.False(switches.IsEnabled(typeof(CountingHandler).Assembly));
        Assert.DoesNotContain(CountingHandler.Type, registry.TaskTypeNames);
        Assert.True(registry.IsRegistered(CountingHandler.Type)); // still installed, so its tasks can be edited
        Assert.True(switches.IsEnabled(typeof(SchedulerEngine).Assembly)); // the core is never switched off
    }

    [Fact]
    public async Task TasksOfASwitchedOffModule_WaitAndRunOnceItIsSwitchedOnAgain()
    {
        var module = new TestModule("Test module");
        await using var host = new InMemoryHost(services =>
        {
            services.AddSchedulerModules(module);
            services.AddSingleton<IDatabaseInitializer, NoDatabaseInitializer>();
        });
        var schedulerHost = host.Get<SchedulerHost>();
        var taskId = await host.CreateManualTaskAsync(CountingHandler.Type);

        await schedulerHost.StartAsync();
        try
        {
            var off = await schedulerHost.SetModuleEnabledAsync("Test module", false, "tester");
            Assert.False(off.Status.Enabled);
            Assert.Equal([false], module.EnabledChanges);

            Assert.True(await host.Get<ISchedulerClient>().RunNowAsync(host.NameOf(taskId)));
            await host.IdleCyclesAsync();
            Assert.Empty(host.Statuses(taskId));

            var on = await schedulerHost.SetModuleEnabledAsync("Test module", true, "tester");
            Assert.True(on.Status.Enabled);
            await InMemoryHost.WaitUntilAsync(() => host.Statuses(taskId).Contains(ExecutionStatus.Success), "the waiting run ran");
            Assert.Equal([false, true], module.EnabledChanges);
            Assert.Contains(host.Logger.Entries, e => e.Message.Contains("'Test module' was switched off by tester"));
        }
        finally
        {
            await schedulerHost.StopAsync();
        }
    }

    [Fact]
    public async Task AModuleSwitchedOffAtStartUp_IsNotStarted_UntilItIsSwitchedOn()
    {
        var module = new TestModule("Test module");
        await using var host = new InMemoryHost(services =>
        {
            services.AddSchedulerModules(module);
            services.AddSingleton<IDatabaseInitializer, NoDatabaseInitializer>();
            services.AddSingleton<IModuleStateStore>(new FixedStore("Test module"));
        });
        var schedulerHost = host.Get<SchedulerHost>();

        await schedulerHost.StartAsync();
        try
        {
            var status = Assert.Single(schedulerHost.Modules);
            Assert.False(status.Enabled);
            Assert.False(status.Initialized);
            Assert.Equal(0, module.Initializations);

            var on = await schedulerHost.SetModuleEnabledAsync("Test module", true);
            Assert.True(on.Status.Initialized);
            Assert.Equal(1, module.Initializations);
        }
        finally
        {
            await schedulerHost.StopAsync();
        }
    }

    [Fact]
    public async Task SwitchingOff_StopsTheModulesApplication_AndSwitchingOnLaunchesItAgain()
    {
        var module = new TestModule("Test module");
        var application = new TestApplication("Test module") { Running = true };
        await using var host = new InMemoryHost(services =>
        {
            services.AddSchedulerModules(module);
            services.AddSingleton<IDatabaseInitializer, NoDatabaseInitializer>();
            services.AddSingleton<IManagedApplication>(application);
        });
        var schedulerHost = host.Get<SchedulerHost>();
        await schedulerHost.StartAsync();
        try
        {
            var off = await schedulerHost.SetModuleEnabledAsync("Test module", false);
            Assert.False(application.Running);
            Assert.Equal("stopped", off.ApplicationMessage);

            var on = await schedulerHost.SetModuleEnabledAsync("Test module", true);
            Assert.True(application.Running);
            Assert.Equal("started", on.ApplicationMessage);

            // An application that was not running when the module was switched off stays stopped.
            await application.StopAsync();
            await schedulerHost.SetModuleEnabledAsync("Test module", false);
            await schedulerHost.SetModuleEnabledAsync("Test module", true);
            Assert.False(application.Running);
        }
        finally
        {
            await schedulerHost.StopAsync();
        }
    }

    [Fact]
    public async Task ARequiredModule_CannotBeSwitchedOff_AndUnknownNamesInTheFileAreIgnored()
    {
        var required = new TestModule("Sign-in", canBeDisabled: false);
        await using var host = new InMemoryHost(services =>
        {
            services.AddSchedulerModules(required);
            services.AddSingleton<IModuleStateStore>(new FixedStore("Sign-in", "Removed module"));
        });
        var switches = host.Get<IModuleSwitches>();

        Assert.True(switches.IsEnabled("Sign-in"));
        Assert.Empty(switches.DisabledModules);
        await Assert.ThrowsAsync<InvalidOperationException>(() => switches.SetEnabledAsync("Sign-in", false));
        await Assert.ThrowsAsync<ArgumentException>(() => switches.SetEnabledAsync("Removed module", false));
        Assert.False(new Scheduler.Modules.Security.SecurityModule().CanBeDisabled);
    }

    [Fact]
    public async Task SchedulerClient_PausesAndResumesATaskByKeyOrName_ButLeavesADisabledTaskAlone()
    {
        await using var host = new InMemoryHost();
        var client = host.Get<ISchedulerClient>();
        var taskId = await host.CreateManualTaskAsync(CountingHandler.Type, "Pause me");

        Assert.Equal(ScheduledTaskStatus.Active, await client.GetTaskStatusAsync("Pause me"));
        Assert.True(await client.SetTaskActiveAsync("Pause me", false));
        Assert.Equal(ScheduledTaskStatus.Paused, await client.GetTaskStatusAsync("Pause me"));
        Assert.True(await client.SetTaskActiveAsync("Pause me", true));
        Assert.Equal(ScheduledTaskStatus.Active, host.TaskOf(taskId).Status);

        host.TaskOf(taskId).Disable(DateTime.UtcNow, null);
        Assert.False(await client.SetTaskActiveAsync("Pause me", true));
        Assert.Null(await client.GetTaskStatusAsync("No such task"));
    }

    [Fact]
    public async Task InventoryLaunchAndStop_ResumeAndPauseItsThreeTasks()
    {
        await using var host = new InMemoryHost(services =>
        {
            new DemoApps.InventoryMonitor.InventoryModule().Register(services);
            services.AddSingleton<IModuleDatabases, NoModuleDatabases>(); // the store is created but never connects
        });
        var client = host.Get<ISchedulerClient>();
        foreach (var (key, type) in new[]
                 {
                     (DemoApps.InventoryMonitor.InventoryModule.StockReportKey, "GenerateStockReport"),
                     (DemoApps.InventoryMonitor.InventoryModule.LowStockCheckKey, "LowStockCheck"),
                     (DemoApps.InventoryMonitor.InventoryModule.WeeklySummaryKey, "WeeklySummary")
                 })
        {
            await client.EnsureTaskAsync(new TaskDefinition { Name = key, SystemKey = key, TaskType = type, CronExpression = "0 0 1 1 *" });
        }

        var inventory = host.Get<IEnumerable<IManagedApplication>>().Single();
        Assert.Equal("Inventory Monitor", inventory.ModuleName);
        Assert.True((await inventory.GetStatusAsync()).IsRunning);

        Assert.True((await inventory.StopAsync()).Succeeded);
        Assert.False((await inventory.GetStatusAsync()).IsRunning);
        Assert.Equal(ScheduledTaskStatus.Paused, await client.GetTaskStatusAsync(DemoApps.InventoryMonitor.InventoryModule.WeeklySummaryKey));

        Assert.True((await inventory.StartAsync()).Succeeded);
        Assert.True((await inventory.GetStatusAsync()).IsRunning);
        Assert.Equal(2, host.Database.Locked(() => host.Database.Queue.Count)); // a stock report and a low-stock check, straight away
    }

    [Fact]
    public async Task SettingsFile_KeepsTheSwitchedOffModules_WithTheOtherSettings()
    {
        var file = new MachineSettingsFile(Path.Combine(_folder, MachineSettingsFile.FileName));
        file.EnsureExists();
        Assert.Empty(file.GetDisabledModules());

        await file.SaveAsync(new Application.Configuration.MachineSettings { BackupFolder = "E:\\Backups" });
        await file.SaveDisabledModulesAsync(["Inventory Monitor"]);
        await file.SaveAsync(file.Get() with { LogFolder = "E:\\Logs" });

        Assert.Equal(["Inventory Monitor"], file.GetDisabledModules());
        Assert.Equal("E:\\Backups", file.Get().BackupFolder);
        Assert.Equal("E:\\Logs", file.Get().LogFolder);
    }

    [Fact]
    public void MaintenanceAgentLauncher_FindsTheProjectAboveTheApplication_AndRunsItOnTheConfiguredPort()
    {
        var project = Path.Combine(_folder, "demos", "DemoApps.MaintenanceAgent", DemoApps.MaintenanceAgentConnector.MaintenanceAgentLauncher.ProjectFile);
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(project, "<Project />");
        var deep = Directory.CreateDirectory(Path.Combine(_folder, "src", "Scheduler.Admin", "bin", "Debug")).FullName;

        Assert.Equal(project, DemoApps.MaintenanceAgentConnector.MaintenanceAgentLauncher.FindProject(deep));
        Assert.Null(DemoApps.MaintenanceAgentConnector.MaintenanceAgentLauncher.FindProject(Path.GetTempPath()));

        var configuration = new Microsoft.Extensions.Configuration.ConfigurationManager
        {
            ["MaintenanceAgent:Url"] = "http://127.0.0.1:5099",
            [DemoApps.MaintenanceAgentConnector.MaintenanceAgentLauncher.LaunchPathSetting] = Path.GetDirectoryName(project)
        };
        using var launcher = new DemoApps.MaintenanceAgentConnector.MaintenanceAgentLauncher(
            new DemoApps.MaintenanceAgentConnector.AgentClient(configuration), configuration);

        var start = launcher.CreateStartInfo();
        Assert.NotNull(start);
        Assert.Equal(["run", "--project", project, "--no-launch-profile", "--", "--Agent:Port=5099"], start.ArgumentList);
        Assert.Equal(Path.GetDirectoryName(project), start.WorkingDirectory);
        Assert.True(start.CreateNoWindow);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class TestModule(string name, bool canBeDisabled = true) : ISchedulerModule
    {
        public string Name => name;
        public bool CanBeDisabled => canBeDisabled;
        public IReadOnlyList<ModuleMenuItem> MenuItems => [];
        public int Initializations { get; private set; }
        public List<bool> EnabledChanges { get; } = [];

        public void Register(IServiceCollection services)
        {
        }

        public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            Initializations++;
            return Task.CompletedTask;
        }

        public Task OnEnabledChangedAsync(IServiceProvider services, bool enabled, CancellationToken cancellationToken)
        {
            EnabledChanges.Add(enabled);
            return Task.CompletedTask;
        }
    }

    private sealed class TestApplication(string moduleName) : IManagedApplication
    {
        public bool Running { get; set; }
        public string ModuleName => moduleName;
        public string DisplayName => "Test application";

        public Task<ManagedApplicationStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ManagedApplicationStatus(Running ? ManagedApplicationState.Running : ManagedApplicationState.Stopped, ""));

        public Task<ManagedApplicationResult> StartAsync(CancellationToken cancellationToken = default)
        {
            Running = true;
            return Task.FromResult(ManagedApplicationResult.Success("started"));
        }

        public Task<ManagedApplicationResult> StopAsync(CancellationToken cancellationToken = default)
        {
            Running = false;
            return Task.FromResult(ManagedApplicationResult.Success("stopped"));
        }
    }

    private sealed class FixedStore(params string[] disabled) : IModuleStateStore
    {
        public IReadOnlyCollection<string> GetDisabledModules() => disabled;

        public Task SaveDisabledModulesAsync(IReadOnlyCollection<string> moduleNames, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoModuleDatabases : IModuleDatabases
    {
        public System.Data.Common.DbDataSource Get(string configurationSection, string defaultDatabaseName) => throw new NotSupportedException();

        public string GetDatabaseName(string configurationSection, string defaultDatabaseName) => defaultDatabaseName;
    }

    private sealed class NoDatabaseInitializer : IDatabaseInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
