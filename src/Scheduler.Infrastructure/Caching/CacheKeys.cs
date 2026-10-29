namespace Scheduler.Infrastructure.Caching;

internal static class CacheKeys
{
    public const string Settings = "gmss:settings";
    public const string LoggingProviders = "gmss:logging-providers";

    /// <summary>Cached values are refreshed at least this often, so a change made by another process is picked up (FR-39).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
}
