using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Scheduler.Application.Backup;
using Scheduler.Infrastructure.Configuration;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Infrastructure.Backup;

/// <summary>The "Backup" section: the databases a backup set can include (WP-5c).</summary>
public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    /// <summary>Empty: the scheduler's database, inventory_demo and maintenance_demo.</summary>
    public List<string> Databases { get; set; } = [];
}

/// <summary>
/// Backup sets (WP-5c): one pg_dump custom-format file per database in a folder named after the kind, date and time,
/// each verified with pg_restore --list and recorded with its SHA-256 checksum in the set's manifest.json, which is
/// kept outside the database (Phase 4, Sections 8.7 and 8.8). Verified sets are copied to the optional copy folder.
/// </summary>
internal sealed class PgBackupService(
    DatabaseConnection database,
    IOptionsMonitor<PathsOptions> paths,
    IOptions<BackupOptions> backupOptions,
    TimeProvider clock,
    ILogger<PgBackupService> logger) : IBackupService
{
    public const string ManifestName = "manifest.json";
    private const string SetPrefix = "gsst_";
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SafetyBackupAge = TimeSpan.FromDays(7);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public string BackupFolder => paths.CurrentValue.ResolveBackupFolder();

    public string? BackupCopyFolder => paths.CurrentValue.ResolveBackupCopyFolder();

    public string LogFolder => paths.CurrentValue.ResolveLogFolder();

    public string SchedulerDatabase => database.Options.Name;

    public IReadOnlyList<string> KnownDatabases =>
        backupOptions.Value.Databases is { Count: > 0 } configured
            ? configured.Distinct(StringComparer.Ordinal).ToList()
            : [database.Options.Name, "inventory_demo", "maintenance_demo"];

    public PostgresToolsInfo FindTools()
    {
        var setting = paths.CurrentValue.PostgresBinFolder;
        var folder = PostgresTools.FindFolder(setting);
        return folder is not null ? new PostgresToolsInfo(folder, null)
            : string.IsNullOrWhiteSpace(setting) ? new PostgresToolsInfo(null, PostgresToolsInfo.NotFoundMessage)
            : new PostgresToolsInfo(null, $"pg_dump was not found in {setting}. Set the PostgreSQL tools folder on the Configuration page.");
    }

    public async Task<BackupSetInfo> CreateBackupAsync(BackupKind kind, IReadOnlyCollection<string>? databases = null, string? folder = null,
        CancellationToken cancellationToken = default)
    {
        var tools = FindTools();
        if (!tools.Found)
        {
            throw new BackupException(tools.Error!);
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var warnings = new List<string>();
            var wanted = (databases ?? KnownDatabases).Distinct(StringComparer.Ordinal).ToList();
            var existing = await ExistingDatabasesAsync(wanted, cancellationToken);
            foreach (var missing in wanted.Except(existing, StringComparer.Ordinal).ToList())
            {
                if (databases is not null)
                {
                    throw new BackupException($"The database {missing} does not exist on this PostgreSQL server.");
                }

                warnings.Add($"The database {missing} does not exist, so it was not backed up.");
                wanted.Remove(missing);
            }

            if (wanted.Count == 0)
            {
                throw new BackupException("None of the databases to back up exist on this PostgreSQL server.");
            }

            var root = folder ?? BackupFolder;
            var now = clock.GetUtcNow().UtcDateTime;
            var setName = $"{Prefix(kind)}{now.ToLocalTime():yyyyMMdd_HHmmss}";
            var setFolder = Path.Combine(root, setName);
            for (var n = 2; Directory.Exists(setFolder); n++)
            {
                setFolder = Path.Combine(root, $"{setName}_{n}");
            }

            setName = Path.GetFileName(setFolder);
            Directory.CreateDirectory(setFolder);
            var files = new List<BackupFileInfo>();
            foreach (var name in wanted)
            {
                var fileName = name + ".dump";
                var path = Path.Combine(setFolder, fileName);
                var dump = await RunAsync(tools.Folder!, "pg_dump", ["--format=custom", "--file", path, .. ConnectionArguments(name)], cancellationToken);
                if (dump.ExitCode != 0)
                {
                    TryDeleteFolder(setFolder);
                    throw new BackupException($"The backup of {name} could not be created. See the error log for the pg_dump output.", dump.Error);
                }

                var list = await RunAsync(tools.Folder!, "pg_restore", ["--list", path], cancellationToken);
                if (list.ExitCode != 0)
                {
                    logger.LogWarning("Backup file {File} was created but pg_restore --list could not read it: {Error}", path, list.Error);
                }

                files.Add(new BackupFileInfo(name, fileName, new FileInfo(path).Length, list.ExitCode == 0, await HashAsync(path, cancellationToken),
                    list.ExitCode == 0 ? null : "pg_restore --list could not read the file."));
            }

            var set = new BackupSetInfo(setName, setFolder, now, kind, files);
            await WriteManifestAsync(set, cancellationToken);

            if (set.Verified && BackupCopyFolder is { } copyRoot && !SameFolder(copyRoot, root))
            {
                if (await CopyAsync(set, copyRoot, cancellationToken) is { } copyWarning)
                {
                    warnings.Add(copyWarning);
                }
            }

            return set with { Warnings = warnings };
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyList<BackupSetInfo>> ListAsync(string? folder = null, CancellationToken cancellationToken = default)
    {
        var root = folder ?? BackupFolder;
        var sets = new List<BackupSetInfo>();
        foreach (var setFolder in SafeDirectories(root))
        {
            if (await ReadManifestAsync(setFolder, cancellationToken) is { } set)
            {
                sets.Add(set);
            }
        }

        return sets.OrderByDescending(s => s.CreatedAtUtc).ToList();
    }

    public async Task<BackupSetInfo?> FindAsync(string setName, string? folder = null, CancellationToken cancellationToken = default)
    {
        // Only a bare folder name inside the backup folder, so a name cannot point anywhere else.
        if (string.IsNullOrWhiteSpace(setName) || Path.GetFileName(setName) != setName || setName is "." or "..")
        {
            return null;
        }

        var setFolder = Path.Combine(folder ?? BackupFolder, setName);
        return Directory.Exists(setFolder) ? await ReadManifestAsync(setFolder, cancellationToken) : null;
    }

    public async Task<string?> CheckChecksumsAsync(BackupSetInfo set, IReadOnlyCollection<string> databases, CancellationToken cancellationToken = default)
    {
        foreach (var name in databases)
        {
            var file = set.Files.FirstOrDefault(f => f.Database == name);
            var path = file is null ? null : Path.Combine(set.Folder, file.FileName);
            if (file is null || !File.Exists(path))
            {
                return $"The backup file of {name} is missing from {set.SetName}.";
            }

            if (file.Sha256.Length == 0 || !string.Equals(file.Sha256, await HashAsync(path!, cancellationToken), StringComparison.OrdinalIgnoreCase))
            {
                return $"The backup file of {name} is damaged: its checksum does not match the manifest.";
            }
        }

        return null;
    }

    public async Task RestoreAsync(BackupSetInfo set, string databaseName, CancellationToken cancellationToken = default)
    {
        var tools = FindTools();
        if (!tools.Found)
        {
            throw new BackupException(tools.Error!);
        }

        var file = set.Files.FirstOrDefault(f => f.Database == databaseName)
                   ?? throw new BackupException($"The backup {set.SetName} does not contain {databaseName}.");
        var path = Path.Combine(set.Folder, file.FileName);
        await Gate.WaitAsync(cancellationToken);
        try
        {
            // Idle pooled connections are closed first so the restore is not blocked by them.
            NpgsqlConnection.ClearAllPools();
            var restore = await RunAsync(tools.Folder!, "pg_restore",
                ["--clean", "--if-exists", "--single-transaction", "--no-owner", "--exit-on-error", .. ConnectionArguments(databaseName), path],
                cancellationToken);
            NpgsqlConnection.ClearAllPools();
            if (restore.ExitCode != 0)
            {
                throw new BackupException($"The restore of {databaseName} failed and it was left unchanged.", restore.Error);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<int> ApplyRetentionAsync(int keepCount, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var removed = 0;
            foreach (var root in new[] { BackupFolder, BackupCopyFolder }.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var sets = await ListAsync(root, cancellationToken);
                var now = clock.GetUtcNow().UtcDateTime;
                var remove = sets.Where(s => s.Kind is BackupKind.Manual or BackupKind.Scheduled).OrderByDescending(s => s.CreatedAtUtc)
                    .Skip(Math.Max(1, keepCount))
                    .Concat(sets.Where(s => s.Kind is BackupKind.PreRestore or BackupKind.PreMigration && now - s.CreatedAtUtc > SafetyBackupAge));
                foreach (var set in remove)
                {
                    if (TryDeleteFolder(set.Folder))
                    {
                        removed++;
                    }
                }
            }

            return removed;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string Prefix(BackupKind kind) => kind switch
    {
        BackupKind.PreRestore => SetPrefix + "prerestore_",
        BackupKind.PreMigration => SetPrefix + "premigration_",
        _ => SetPrefix + "backup_"
    };

    /// <summary>Copies a verified set and checks each copy's checksum. Returns a warning, or null when the copy is good (WP-5d).</summary>
    private async Task<string?> CopyAsync(BackupSetInfo set, string copyRoot, CancellationToken cancellationToken)
    {
        var target = Path.Combine(copyRoot, set.SetName);
        try
        {
            Directory.CreateDirectory(target);
            foreach (var file in set.Files)
            {
                var copy = Path.Combine(target, file.FileName);
                File.Copy(Path.Combine(set.Folder, file.FileName), copy, overwrite: true);
                if (!string.Equals(file.Sha256, await HashAsync(copy, cancellationToken), StringComparison.OrdinalIgnoreCase))
                {
                    return $"The copy of {file.FileName} in {copyRoot} does not match the original; the backup itself is fine.";
                }
            }

            File.Copy(Path.Combine(set.Folder, ManifestName), Path.Combine(target, ManifestName), overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Backup set {Set} could not be copied to {Folder}.", set.SetName, copyRoot);
            return $"The backup was made but could not be copied to {copyRoot}: {ex.Message}";
        }
    }

    private async Task<IReadOnlyList<string>> ExistingDatabasesAsync(IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT datname FROM pg_database WHERE datname = ANY(@names)", connection);
            command.Parameters.AddWithValue("names", names.ToArray());
            var found = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                found.Add(reader.GetString(0));
            }

            return found;
        }
        catch (NpgsqlException ex)
        {
            throw new BackupException("The database server could not be reached, so no backup was made.", ex.Message, ex);
        }
    }

    private string[] ConnectionArguments(string databaseName)
    {
        var o = database.Options;
        return ["--host", o.Host, "--port", o.Port.ToString(CultureInfo.InvariantCulture), "--username", o.Username, "--dbname", databaseName, "--no-password"];
    }

    private async Task<(int ExitCode, string Error)> RunAsync(string folder, string tool, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Path.Combine(folder, PostgresTools.ToolFile(tool)))
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // The password goes to the child process's environment, never its command line (Phase 4, Section 8.5).
        if (database.HasPassword)
        {
            start.Environment["PGPASSWORD"] = database.Password;
        }

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new BackupException($"{tool} could not be started. Set the PostgreSQL tools folder on the Configuration page.", ex.Message, ex);
        }

        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ToolTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new BackupException($"{tool} was stopped because it was cancelled or took longer than {ToolTimeout.TotalMinutes} minutes.");
        }

        await output;
        return (process.ExitCode, (await error).Trim());
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private async Task<BackupSetInfo?> ReadManifestAsync(string setFolder, CancellationToken cancellationToken)
    {
        var path = Path.Combine(setFolder, ManifestName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var manifest = await JsonSerializer.DeserializeAsync<Manifest>(stream, JsonOptions, cancellationToken);
            if (manifest is null)
            {
                return null;
            }

            // A file deleted or moved by hand is shown as not verified.
            var files = manifest.Files
                .Select(f => File.Exists(Path.Combine(setFolder, f.FileName)) ? f : f with { Verified = false, Error = "The file is missing." })
                .ToList();
            return new BackupSetInfo(Path.GetFileName(setFolder), setFolder, manifest.CreatedAtUtc, manifest.Kind, files);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            logger.LogWarning(ex, "The manifest of backup set {Folder} could not be read.", setFolder);
            return null;
        }
    }

    private static async Task WriteManifestAsync(BackupSetInfo set, CancellationToken cancellationToken)
    {
        var manifest = new Manifest(set.SetName, set.CreatedAtUtc, set.Kind, Environment.MachineName, [.. set.Files]);
        var path = Path.Combine(set.Folder, ManifestName);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? Directory.GetDirectories(folder, SetPrefix + "*")
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Deletes a backup set folder; only folders named gsst_* that hold a manifest are ever deleted.</summary>
    private bool TryDeleteFolder(string folder)
    {
        try
        {
            if (Path.GetFileName(folder).StartsWith(SetPrefix, StringComparison.Ordinal) && Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Backup set {Folder} could not be deleted.", folder);
        }

        return false;
    }

    /// <summary>The manifest.json of a set.</summary>
    private sealed record Manifest(string SetName, DateTime CreatedAtUtc, BackupKind Kind, string Machine, List<BackupFileInfo> Files);
}
