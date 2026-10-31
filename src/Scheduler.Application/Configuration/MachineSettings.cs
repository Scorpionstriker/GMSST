using Scheduler.Application.Backup;
using Scheduler.Application.Common;
using Scheduler.Domain.Enums;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.Application.Configuration;

/// <summary>
/// Settings that belong to this computer rather than to the database (WP-5a): folders, the PostgreSQL tools folder,
/// the developer login bypass and the Maintenance Agent's address. They are kept in the user settings file
/// (%LOCALAPPDATA%\GSST\appsettings.user.json on Windows), so a restored database never brings another computer's
/// folders with it. Empty values use the defaults.
/// </summary>
public sealed record MachineSettings
{
    public string PostgresBinFolder { get; init; } = "";
    public string BackupFolder { get; init; } = "";
    public string BackupCopyFolder { get; init; } = "";
    public string LogFolder { get; init; } = "";
    public bool AllowLoginBypass { get; init; }
    public string MaintenanceAgentUrl { get; init; } = "";
}

/// <summary>Reads and writes the user settings file. Implemented in the infrastructure layer.</summary>
public interface IMachineSettingsStore
{
    /// <summary>Full path of the user settings file.</summary>
    string FilePath { get; }

    MachineSettings Get();

    /// <summary>Writes the file; the configuration reloads it, so the new values apply without a restart.</summary>
    Task SaveAsync(MachineSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>The "This computer" part of the Configuration page.</summary>
public sealed record GetMachineSettingsQuery : IRequireAdministrator;

/// <param name="Settings">The values in the user settings file.</param>
/// <param name="FilePath">Where the file is.</param>
/// <param name="Tools">Where pg_dump and pg_restore were found, or why not.</param>
/// <param name="EffectiveBackupFolder">The backup folder in use (the default when the setting is empty).</param>
/// <param name="EffectiveLogFolder">The log folder in use.</param>
public sealed record MachineSettingsView(MachineSettings Settings, string FilePath, PostgresToolsInfo Tools, string EffectiveBackupFolder,
    string EffectiveLogFolder);

public sealed record UpdateMachineSettingsCommand(MachineSettings Settings) : IRequireAdministrator;

internal sealed class GetMachineSettingsHandler(IMachineSettingsStore store, IBackupService backups)
    : IQueryHandler<GetMachineSettingsQuery, MachineSettingsView>
{
    public Task<Result<MachineSettingsView>> HandleAsync(GetMachineSettingsQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(new MachineSettingsView(store.Get(), store.FilePath, backups.FindTools(), backups.BackupFolder, backups.LogFolder)));
}

internal sealed class UpdateMachineSettingsValidator : IValidator<UpdateMachineSettingsCommand>
{
    public Task<IReadOnlyList<Error>> ValidateAsync(UpdateMachineSettingsCommand command, CancellationToken cancellationToken = default)
    {
        var s = command.Settings;
        var errors = new ValidationErrors()
            .AddIf(!IsEmptyOrFullPath(s.PostgresBinFolder), nameof(s.PostgresBinFolder), "Enter a full folder path, or leave it empty to search for the tools.")
            .AddIf(!IsEmptyOrFullPath(s.BackupFolder), nameof(s.BackupFolder), "Enter a full folder path, or leave it empty for the default folder.")
            .AddIf(!IsEmptyOrFullPath(s.BackupCopyFolder), nameof(s.BackupCopyFolder), "Enter a full folder path, or leave it empty to make no copies.")
            .AddIf(!IsEmptyOrFullPath(s.LogFolder), nameof(s.LogFolder), "Enter a full folder path, or leave it empty for the default folder.")
            .AddIf(!IsEmptyOrLoopbackUrl(s.MaintenanceAgentUrl), nameof(s.MaintenanceAgentUrl),
                "Enter an address on this computer, for example http://127.0.0.1:5087, or leave it empty for the default.");
        return Task.FromResult(errors.Errors);
    }

    private static bool IsEmptyOrFullPath(string? path) =>
        string.IsNullOrWhiteSpace(path) || (path.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(path.Trim())));

    internal static bool IsEmptyOrLoopbackUrl(string? url) =>
        string.IsNullOrWhiteSpace(url)
        || (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp
            && (uri.IsLoopback || (System.Net.IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && System.Net.IPAddress.IsLoopback(address))));
}

/// <summary>Saves the user settings file. Only a Developer can change the login bypass (WP-4a).</summary>
internal sealed class UpdateMachineSettingsHandler(IMachineSettingsStore store, ICurrentUser currentUser, ISchedulerLogger log)
    : ICommandHandler<UpdateMachineSettingsCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdateMachineSettingsCommand command, CancellationToken cancellationToken = default)
    {
        var before = store.Get();
        var after = command.Settings with
        {
            PostgresBinFolder = command.Settings.PostgresBinFolder.Trim(),
            BackupFolder = command.Settings.BackupFolder.Trim(),
            BackupCopyFolder = command.Settings.BackupCopyFolder.Trim(),
            LogFolder = command.Settings.LogFolder.Trim(),
            MaintenanceAgentUrl = command.Settings.MaintenanceAgentUrl.Trim()
        };

        if (before.AllowLoginBypass != after.AllowLoginBypass && !currentUser.IsDeveloper)
        {
            return Result.Failure<Unit>(Error.Forbidden);
        }

        await store.SaveAsync(after, cancellationToken);
        var user = currentUser.Username ?? "the local user";
        await log.LogAsync(SchedulerLogLevel.Info, LogSources.Admin, $"The settings of this computer were changed by {user}.",
            properties: new Dictionary<string, object?> { ["file"] = store.FilePath, ["userId"] = currentUser.UserId }, cancellationToken: cancellationToken);

        if (before.AllowLoginBypass != after.AllowLoginBypass)
        {
            await log.LogAsync(SchedulerLogLevel.Warning, LogSources.Security,
                after.AllowLoginBypass ? $"Login bypass (developer mode) was turned on by {user}." : $"Login bypass (developer mode) was turned off by {user}.",
                properties: new Dictionary<string, object?>
                {
                    ["action"] = after.AllowLoginBypass ? "LoginBypassOn" : "LoginBypassOff",
                    ["user"] = user,
                    ["userId"] = currentUser.UserId,
                    ["allowLoginBypass"] = after.AllowLoginBypass
                },
                cancellationToken: cancellationToken);
        }

        return Result.Success(Unit.Value);
    }
}
