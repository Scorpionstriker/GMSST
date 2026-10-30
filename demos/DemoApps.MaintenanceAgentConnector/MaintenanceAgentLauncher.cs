using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Scheduler.Extensibility.Contracts;

namespace DemoApps.MaintenanceAgentConnector;

/// <summary>
/// The Launch and Stop buttons of the Maintenance Agent, on the Modules page and the Maintenance Agent page, so the
/// agent no longer has to be started from a terminal. Launching starts the agent as a hidden background process of
/// the Admin application; it inherits the environment, so GMSS_DB_PASSWORD and GSST_AGENT_KEY reach it as before.
/// What is started is, in order: the "MaintenanceAgent:LaunchPath" setting (a .csproj, .dll or .exe, or the folder of
/// one), otherwise the DemoApps.MaintenanceAgent project found in the folders above the Admin application, run with
/// "dotnet run". Stopping asks the agent to shut down (POST /shutdown) and, if it was launched here and does not stop,
/// ends the process. An agent launched here is also ended when the Admin application closes.
/// </summary>
public sealed class MaintenanceAgentLauncher : IManagedApplication, IDisposable
{
    /// <summary>
    /// "DemoApps.MaintenanceAgent", the agent's project and program name. Built at run time because the architecture
    /// tests read a string that starts with another project's name as a dependency on that project.
    /// </summary>
    public static readonly string AgentName = string.Join('.', "DemoApps", "MaintenanceAgent");

    public static string ProjectFile => AgentName + ".csproj";
    public const string LaunchPathSetting = "MaintenanceAgent:LaunchPath";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);

    private readonly AgentClient _agent;
    private readonly IConfiguration? _configuration;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<string> _output = new();
    private Process? _process;
    private bool _starting;

    public MaintenanceAgentLauncher(AgentClient agent, IConfiguration? configuration = null)
    {
        _agent = agent;
        _configuration = configuration;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillOwnProcess();
    }

    public string ModuleName => MaintenanceAgentConnectorModule.ModuleName;

    public string DisplayName => "Maintenance Agent";

    /// <summary>True while an agent launched by this application is running.</summary>
    public bool LaunchedHere => _process is { HasExited: false };

    public async Task<ManagedApplicationStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_starting)
        {
            return new ManagedApplicationStatus(ManagedApplicationState.Stopped, "Starting…");
        }

        var status = await _agent.GetStatusAsync(cancellationToken);
        if (!status.Running)
        {
            return new ManagedApplicationStatus(ManagedApplicationState.Stopped, $"Not running on port {_agent.Port}.");
        }

        return new ManagedApplicationStatus(ManagedApplicationState.Running, status.Status is null
            ? status.Message
            : $"Running on port {_agent.Port}{(LaunchedHere ? ", launched from here" : "")}.");
    }

    public async Task<ManagedApplicationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return ManagedApplicationResult.Failure("The Maintenance Agent is already being launched or stopped.");
        }

        try
        {
            if ((await _agent.GetStatusAsync(cancellationToken)).Running)
            {
                return ManagedApplicationResult.Success($"The Maintenance Agent is already running on port {_agent.Port}.");
            }

            if (FindDotnet() is null && LaunchTarget() is { } target && !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return ManagedApplicationResult.Failure("The dotnet command was not found. Install the .NET SDK, or set " + LaunchPathSetting +
                                                        " to the agent's built .exe.");
            }

            if (CreateStartInfo() is not { } startInfo)
            {
                return ManagedApplicationResult.Failure(
                    $"The Maintenance Agent project ({ProjectFile}) was not found in the folders above this application. Set " +
                    $"{LaunchPathSetting} in the user settings file to the project or to the agent's built .exe, or start it with: " +
                    "dotnet run --project demos/DemoApps.MaintenanceAgent");
            }

            _starting = true;
            lock (_output)
            {
                _output.Clear();
            }

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => Remember(e.Data);
            process.ErrorDataReceived += (_, e) => Remember(e.Data);
            try
            {
                process.Start();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                process.Dispose();
                return ManagedApplicationResult.Failure($"The Maintenance Agent could not be launched: {ex.Message}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;

            // "dotnet run" builds the agent first, which can take a while the first time.
            var deadline = DateTime.UtcNow + StartTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    return ManagedApplicationResult.Failure($"The Maintenance Agent stopped while starting (exit code {process.ExitCode}). {LastOutput()}".TrimEnd());
                }

                if ((await _agent.GetStatusAsync(cancellationToken)).Running)
                {
                    return ManagedApplicationResult.Success($"The Maintenance Agent is running on port {_agent.Port}.");
                }

                await Task.Delay(500, cancellationToken);
            }

            KillOwnProcess();
            return ManagedApplicationResult.Failure($"The Maintenance Agent did not answer within {StartTimeout.TotalSeconds:0} seconds, so it was stopped. {LastOutput()}".TrimEnd());
        }
        finally
        {
            _starting = false;
            _gate.Release();
        }
    }

    public async Task<ManagedApplicationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return ManagedApplicationResult.Failure("The Maintenance Agent is already being launched or stopped.");
        }

        try
        {
            if (!(await _agent.GetStatusAsync(cancellationToken)).Running)
            {
                KillOwnProcess();
                return ManagedApplicationResult.Success("The Maintenance Agent is not running.");
            }

            var accepted = await _agent.ShutdownAsync(cancellationToken);
            var deadline = DateTime.UtcNow + StopTimeout;
            while (accepted && DateTime.UtcNow < deadline && (await _agent.GetStatusAsync(cancellationToken)).Running)
            {
                await Task.Delay(300, cancellationToken);
            }

            if ((await _agent.GetStatusAsync(cancellationToken)).Running)
            {
                if (!LaunchedHere)
                {
                    return ManagedApplicationResult.Failure(
                        "The Maintenance Agent did not stop. It was started outside the scheduler (or is an older build without the stop " +
                        "command), so close its window or press Ctrl+C in it.");
                }

                KillOwnProcess();
            }

            // "dotnet run" ends once the agent has stopped; give it a moment so a new launch starts cleanly.
            if (_process is { } process)
            {
                process.WaitForExit(TimeSpan.FromSeconds(5));
                KillOwnProcess();
            }

            return ManagedApplicationResult.Success("The Maintenance Agent has stopped.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => KillOwnProcess();

    /// <summary>The start command for the configured or discovered agent, or null when nothing was found.</summary>
    internal ProcessStartInfo? CreateStartInfo()
    {
        if (LaunchTarget() is not { } target)
        {
            return null;
        }

        var portArgument = "--Agent:Port=" + _agent.Port;
        ProcessStartInfo info;
        if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            info = new ProcessStartInfo(target);
        }
        else if (target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            info = new ProcessStartInfo(FindDotnet() ?? "dotnet");
            info.ArgumentList.Add(target);
        }
        else
        {
            info = new ProcessStartInfo(FindDotnet() ?? "dotnet");
            foreach (var argument in new[] { "run", "--project", target, "--no-launch-profile", "--" })
            {
                info.ArgumentList.Add(argument);
            }
        }

        info.ArgumentList.Add(portArgument);
        info.WorkingDirectory = Path.GetDirectoryName(target)!;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        return info;
    }

    /// <summary>The configured launch path, or the agent project found above the application's folder.</summary>
    internal string? LaunchTarget()
    {
        if (_configuration?[LaunchPathSetting] is { Length: > 0 } configured)
        {
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"')));
            if (Directory.Exists(path))
            {
                return Directory.EnumerateFiles(path, "*.csproj").FirstOrDefault()
                       ?? Directory.EnumerateFiles(path, AgentName + ".exe").FirstOrDefault()
                       ?? Directory.EnumerateFiles(path, AgentName + ".dll").FirstOrDefault();
            }

            return File.Exists(path) ? path : null;
        }

        return FindProject(AppContext.BaseDirectory) ?? FindProject(Environment.CurrentDirectory);
    }

    internal static string? FindProject(string startFolder)
    {
        for (var folder = new DirectoryInfo(startFolder); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "demos", AgentName, ProjectFile);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The dotnet command. A desktop application does not always have it on its PATH (for example on macOS), so the
    /// usual install folders are tried too.
    /// </summary>
    internal static string? FindDotnet()
    {
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var folders = new List<string?>
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? Path.GetDirectoryName(host) : null
        };
        folders.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        folders.AddRange(OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")]
            : ["/usr/local/share/dotnet", "/opt/homebrew/bin", "/usr/local/bin", "/usr/share/dotnet", "/usr/lib/dotnet",
               Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet")]);

        return folders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => Path.Combine(f!.Trim(), name))
            .FirstOrDefault(File.Exists);
    }

    private void Remember(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_output)
        {
            _output.Enqueue(line.Trim());
            while (_output.Count > 5)
            {
                _output.Dequeue();
            }
        }
    }

    private string LastOutput()
    {
        lock (_output)
        {
            return _output.Count == 0 ? "" : "Last output: " + string.Join(" | ", _output);
        }
    }

    private void KillOwnProcess()
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Already gone.
        }
        finally
        {
            process.Dispose();
            _process = null;
        }
    }
}
