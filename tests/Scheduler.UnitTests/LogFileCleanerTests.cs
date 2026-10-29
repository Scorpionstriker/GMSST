using Microsoft.Extensions.Options;
using Scheduler.Infrastructure.Configuration;
using Scheduler.Infrastructure.Logging;

namespace Scheduler.UnitTests;

/// <summary>WP-3b: the Log Clean-up task deletes only the File provider's old log files.</summary>
public sealed class LogFileCleanerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gsst-logs-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void OldLogFiles_AreDeleted_OnlyWhenTheyMatchTheFileProviderPattern()
    {
        Directory.CreateDirectory(_folder);
        var now = DateTime.UtcNow;
        var old = Path.Combine(_folder, $"gmss{now.AddDays(-100):yyyyMMdd}.log");
        var recent = Path.Combine(_folder, $"gmss{now:yyyyMMdd}.log");
        var other = Path.Combine(_folder, "notes.log");
        var lookalike = Path.Combine(_folder, "gmssABCDEFGH.log");
        foreach (var file in new[] { old, recent, other, lookalike })
        {
            File.WriteAllText(file, "x");
        }

        var cleaner = new LogFileCleaner(new FixedPaths(new PathsOptions { LogFolder = _folder }));

        Assert.Equal(1, cleaner.DeleteOlderThan(now.AddDays(-90)));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(lookalike));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class FixedPaths(PathsOptions value) : IOptionsMonitor<PathsOptions>
    {
        public PathsOptions CurrentValue => value;
        public PathsOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<PathsOptions, string?> listener) => null;
    }
}
