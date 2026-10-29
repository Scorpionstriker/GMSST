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

/// <summary>
/// Full logical backups with pg_dump in custom format, verified with pg_restore --list and recorded with their
/// SHA-256 checksum in manifest.json, which is kept outside the database (Phase 4, Sections 8.7 and 8.8).
/// </summary>
internal sealed class PgBackupService(
    DatabaseConnection database,
    IOptions<PathsOptions> paths,
    TimeProvider clock,
    ILogger<PgBackupService> logger) : IBackupService
{
    private const string ManifestName = "manifest.json";
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SafetyBackupAge = TimeSpan.FromDays(7);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public string BackupFolder => paths.Value.ResolveBackupFolder();

    public async Task<BackupInfo> CreateBackupAsync(BackupKind kind, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(BackupFolder);
            var now = clock.GetUtcNow().UtcDateTime;
            var fileName = $"{Prefix(kind)}{now.ToLocalTime():yyyyMMdd_HHmmss}.dump";
            var path = Path.Combine(BackupFolder, fileName);
            for (var n = 1; File.Exists(path); n++)
            {
                fileName = $"{Prefix(kind)}{now.ToLocalTime():yyyyMMdd_HHmmss}_{n}.dump";
                path = Path.Combine(BackupFolder, fileName);
            }

            var dump = await RunAsync("pg_dump", ["--format=custom", "--file", path, .. ConnectionArguments()], cancellationToken);
            if (dump.ExitCode != 0)
            {
                TryDelete(path);
                throw new BackupException("The backup could not be created. See the error log for the pg_dump output.", dump.Error);
            }

            var list = await RunAsync("pg_restore", ["--list", path], cancellationToken);
            var info = new BackupInfo(fileName, now, kind, new FileInfo(path).Length, list.ExitCode == 0, await HashAsync(path, cancellationToken));
            var manifest = await ReadManifestAsync(cancellationToken);
            manifest.RemoveAll(b => string.Equals(b.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            manifest.Add(info);
            await WriteManifestAsync(manifest, cancellationToken);

            if (!info.Verified)
            {
                logger.LogWarning("Backup {File} was created but pg_restore --list could not read it: {Error}", fileName, list.Error);
            }

            return info;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(cancellationToken);
        var present = manifest.Where(b => File.Exists(Path.Combine(BackupFolder, b.FileName))).ToList();

        // Files copied into the folder by hand are listed too, as unverified.
        if (Directory.Exists(BackupFolder))
        {
            foreach (var file in Directory.EnumerateFiles(BackupFolder, "*.dump"))
            {
                var name = Path.GetFileName(file);
                if (present.All(b => !string.Equals(b.FileName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    present.Add(new BackupInfo(name, File.GetLastWriteTimeUtc(file), KindOf(name), new FileInfo(file).Length, false, string.Empty));
                }
            }
        }

        return present.OrderByDescending(b => b.CreatedAtUtc).ToList();
    }

    public async Task<bool> VerifyChecksumAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var path = ResolveExisting(fileName);
        var entry = (await ReadManifestAsync(cancellationToken))
            .FirstOrDefault(b => string.Equals(b.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        return path is not null && entry is { Sha256.Length: > 0 }
               && string.Equals(entry.Sha256, await HashAsync(path, cancellationToken), StringComparison.OrdinalIgnoreCase);
    }

    public async Task RestoreAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var path = ResolveExisting(fileName) ?? throw new BackupException("The backup file was not found in the backup folder.");
        await Gate.WaitAsync(cancellationToken);
        try
        {
            // Idle pooled connections are closed first so the restore is not blocked by them.
            NpgsqlConnection.ClearAllPools();
            var restore = await RunAsync("pg_restore",
                ["--clean", "--if-exists", "--single-transaction", "--no-owner", "--exit-on-error", .. ConnectionArguments(), path],
                cancellationToken);
            NpgsqlConnection.ClearAllPools();
            if (restore.ExitCode != 0)
            {
                throw new BackupException("The restore failed and the current data was left unchanged.", restore.Error);
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
            var manifest = await ReadManifestAsync(cancellationToken);
            var now = clock.GetUtcNow().UtcDateTime;
            var normal = manifest.Where(b => b.Kind is BackupKind.Manual or BackupKind.Scheduled).OrderByDescending(b => b.CreatedAtUtc).ToList();
            var remove = normal.Skip(Math.Max(1, keepCount))
                .Concat(manifest.Where(b => b.Kind is BackupKind.PreRestore or BackupKind.PreMigration && now - b.CreatedAtUtc > SafetyBackupAge))
                .ToList();

            foreach (var backup in remove)
            {
                TryDelete(Path.Combine(BackupFolder, backup.FileName));
                manifest.Remove(backup);
            }

            if (remove.Count > 0)
            {
                await WriteManifestAsync(manifest, cancellationToken);
            }

            return remove.Count;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string Prefix(BackupKind kind) => kind switch
    {
        BackupKind.PreRestore => "gmss_prerestore_",
        BackupKind.PreMigration => "gmss_premigration_",
        _ => "gmss_backup_"
    };

    private static BackupKind KindOf(string fileName) =>
        fileName.StartsWith("gmss_prerestore_", StringComparison.OrdinalIgnoreCase) ? BackupKind.PreRestore
        : fileName.StartsWith("gmss_premigration_", StringComparison.OrdinalIgnoreCase) ? BackupKind.PreMigration
        : BackupKind.Manual;

    /// <summary>Accepts only a bare file name inside the backup folder, so a path cannot point anywhere else.</summary>
    private string? ResolveExisting(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName || !fileName.EndsWith(".dump", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = Path.Combine(BackupFolder, fileName);
        return File.Exists(path) ? path : null;
    }

    private string[] ConnectionArguments()
    {
        var o = database.Options;
        return ["--host", o.Host, "--port", o.Port.ToString(CultureInfo.InvariantCulture), "--username", o.Username, "--dbname", o.Name, "--no-password"];
    }

    private async Task<(int ExitCode, string Error)> RunAsync(string tool, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(paths.Value.ResolveTool(tool))
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
            throw new BackupException($"{tool} could not be started. Install the PostgreSQL client tools or set Paths:PostgresBinFolder.",
                ex.Message, ex);
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

    private async Task<List<BackupInfo>> ReadManifestAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(BackupFolder, ManifestName);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<BackupInfo>>(stream, JsonOptions, cancellationToken) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "The backup manifest could not be read; backups are listed as unverified.");
            return [];
        }
    }

    private async Task WriteManifestAsync(List<BackupInfo> manifest, CancellationToken cancellationToken)
    {
        var path = Path.Combine(BackupFolder, ManifestName);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(manifest.OrderBy(b => b.CreatedAtUtc), JsonOptions), Encoding.UTF8,
            cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Backup file {File} could not be deleted.", path);
        }
    }
}
