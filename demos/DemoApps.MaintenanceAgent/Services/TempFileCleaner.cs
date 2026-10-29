using DemoApps.MaintenanceAgent.Data;
using Microsoft.Extensions.Options;

namespace DemoApps.MaintenanceAgent.Services;

/// <summary>The optional JSON body of POST /cleanup.</summary>
public sealed record CleanupRequest(string? Folder = null, int? OlderThanHours = null, string? Pattern = null, bool? IncludeSubfolders = null,
    bool? DryRun = null);

public sealed record CleanupResult(bool Succeeded, string Message, string Folder, int Deleted, int Skipped, long Bytes, bool DryRun);

/// <summary>
/// POST /cleanup (FR-79): deletes files older than OlderThanHours that match Pattern in Folder. The folder must be
/// inside one of the allowed roots in appsettings.json, so a request can never delete files elsewhere on the computer.
/// Each run is recorded in maintenance.cleanup_run.
/// </summary>
public sealed class TempFileCleaner(IOptions<MaintenanceOptions> options, MaintenanceStore store, TimeProvider clock, ILogger<TempFileCleaner> logger)
{
    public async Task<CleanupResult> RunAsync(CleanupRequest request, CancellationToken cancellationToken)
    {
        var result = Clean(request, cancellationToken);
        try
        {
            await store.AddCleanupRunAsync(new CleanupRecord(clock.GetUtcNow().UtcDateTime, result.Succeeded, result.Folder, result.Deleted,
                result.Skipped, result.Bytes, result.DryRun, result.Message), cancellationToken);
        }
        catch (Exception ex) when (MaintenanceStore.IsDatabaseError(ex))
        {
            logger.LogWarning(ex, "The clean-up run could not be recorded.");
        }

        return result;
    }

    internal CleanupResult Clean(CleanupRequest request, CancellationToken cancellationToken)
    {
        var roots = options.Value.ResolveAllowedRoots();
        var folder = string.IsNullOrWhiteSpace(request.Folder) ? roots[0] : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.Folder));
        var dryRun = request.DryRun == true;
        if (!IsInside(folder, roots))
        {
            return Failed($"The folder '{folder}' is not inside an allowed clean-up folder ({string.Join(", ", roots)}).");
        }

        var hours = request.OlderThanHours ?? 24;
        if (hours < 1)
        {
            return Failed("\"olderThanHours\" must be at least 1.");
        }

        var pattern = string.IsNullOrEmpty(request.Pattern) ? "*" : request.Pattern;
        if (pattern.Contains("..") || pattern.IndexOfAny(['/', '\\']) >= 0)
        {
            return Failed("\"pattern\" must be a file name pattern such as *.tmp.");
        }

        Directory.CreateDirectory(folder);
        var cutoff = clock.GetUtcNow().UtcDateTime.AddHours(-hours);
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = request.IncludeSubfolders != false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        int deleted = 0, skipped = 0;
        long bytes = 0;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles(pattern, enumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                logger.LogWarning("Could not delete {File}: {Reason}", file.FullName, ex.Message);
            }
        }

        var verb = dryRun ? "Would delete" : "Deleted";
        logger.LogInformation("{Verb} {Count} file(s), {Bytes} bytes, in {Folder}.", verb, deleted, bytes, folder);
        return new CleanupResult(true, $"{verb} {deleted} file(s) older than {hours} hours; {skipped} could not be deleted.", folder, deleted, skipped,
            bytes, dryRun);

        CleanupResult Failed(string message) => new(false, message, folder, 0, 0, 0, dryRun);
    }

    internal static bool IsInside(string folder, IReadOnlyList<string> roots)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return roots.Any(root => string.Equals(full, root, comparison)
                                 || full.StartsWith(root + Path.DirectorySeparatorChar, comparison));
    }
}
