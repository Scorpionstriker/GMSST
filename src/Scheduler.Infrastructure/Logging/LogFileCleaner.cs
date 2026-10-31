using System.Globalization;
using Microsoft.Extensions.Options;
using Scheduler.Application.Common.Persistence;
using Scheduler.Infrastructure.Configuration;

namespace Scheduler.Infrastructure.Logging;

/// <summary>
/// Deletes the File provider's daily files (gmssYYYYMMDD.log) that are older than the retention period (WP-3b).
/// Only files with exactly that name pattern are touched; the date comes from the file name.
/// </summary>
internal sealed class LogFileCleaner(IOptionsMonitor<PathsOptions> paths) : ILogFileCleaner
{
    public int DeleteOlderThan(DateTime beforeUtc)
    {
        var folder = paths.CurrentValue.ResolveLogFolder();
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(folder, FileLoggingProvider.FilePattern))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Length != 12 || !DateTime.TryParseExact(name[4..], "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var day))
            {
                continue;
            }

            // A file holds one whole day, so it goes only when its last minute is older than the cut-off.
            if (day.AddDays(1) <= beforeUtc)
            {
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file that is open elsewhere is tried again at the next clean-up.
                }
            }
        }

        return deleted;
    }
}
