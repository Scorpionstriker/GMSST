using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scheduler.Extensibility.Contracts;

namespace DemoApps.MaintenanceAgent.Handlers;

/// <summary>
/// Deletes files older than <c>olderThanHours</c> that match <c>pattern</c> in <c>folder</c> (FR-79). The folder must be
/// inside one of the allowed roots in appsettings.json, so a task cannot delete files elsewhere on the computer.
/// </summary>
public sealed class TempFileCleanupHandler(IOptions<MaintenanceOptions> options, TimeProvider clock) : ITaskHandler
{
    public const string Type = "TempFileCleanup";

    public string TaskType => Type;
    public string DisplayName => "Maintenance: temporary-file clean-up";
    public string Description => "Deletes old files in an allowed temporary folder.";
    public string PayloadExample => """{ "folder": "", "olderThanHours": 24, "pattern": "*", "includeSubfolders": true, "dryRun": false }""";

    public Task<TaskResult> ExecuteAsync(ITaskContext context)
    {
        var roots = options.Value.ResolveAllowedRoots();
        var folderText = context.GetString("folder");
        var folder = string.IsNullOrWhiteSpace(folderText) ? roots[0] : Path.GetFullPath(Environment.ExpandEnvironmentVariables(folderText));
        if (!IsInside(folder, roots))
        {
            return Task.FromResult(TaskResult.Failure($"The folder '{folder}' is not inside an allowed clean-up folder ({string.Join(", ", roots)})."));
        }

        var hours = context.GetInt32("olderThanHours") ?? 24;
        if (hours < 1)
        {
            return Task.FromResult(TaskResult.Failure("\"olderThanHours\" must be at least 1."));
        }

        var pattern = context.GetString("pattern") is { Length: > 0 } p ? p : "*";
        if (pattern.Contains("..") || pattern.IndexOfAny(['/', '\\']) >= 0)
        {
            return Task.FromResult(TaskResult.Failure("\"pattern\" must be a file name pattern such as *.tmp."));
        }

        Directory.CreateDirectory(folder);
        var dryRun = context.Payload.TryGetProperty("dryRun", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.True;
        var recursive = !context.Payload.TryGetProperty("includeSubfolders", out var s) || s.ValueKind != System.Text.Json.JsonValueKind.False;
        var cutoff = clock.GetUtcNow().UtcDateTime.AddHours(-hours);
        var enumeration = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

        int deleted = 0, skipped = 0;
        long bytes = 0;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles(pattern, enumeration))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (file.LastWriteTimeUtc >= cutoff)
            {
                continue;
            }

            try
            {
                var size = file.Length;
                if (!dryRun)
                {
                    file.Delete();
                }

                deleted++;
                bytes += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                context.Logger.LogWarning("Could not delete {File}: {Reason}", file.FullName, ex.Message);
            }
        }

        var verb = dryRun ? "Would delete" : "Deleted";
        context.Logger.LogInformation("{Verb} {Count} file(s), {Bytes} bytes, in {Folder}.", verb, deleted, bytes, folder);
        return Task.FromResult(TaskResult.Success($"{verb} {deleted} file(s) older than {hours} hours; {skipped} could not be deleted.",
            new { folder, deleted, skipped, bytes, dryRun }));
    }

    internal static bool IsInside(string folder, IReadOnlyList<string> roots)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return roots.Any(root => string.Equals(full, root, comparison)
                                 || full.StartsWith(root + Path.DirectorySeparatorChar, comparison));
    }
}
