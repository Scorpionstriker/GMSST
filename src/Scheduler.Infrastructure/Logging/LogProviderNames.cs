namespace Scheduler.Infrastructure.Logging;

/// <summary>Names of the built-in providers; they match the provider_name rows seeded in logging_provider.</summary>
public static class LogProviderNames
{
    public const string Database = "DatabaseLoggingProvider";
    public const string File = "FileLoggingProvider";
    public const string Console = "ConsoleLoggingProvider";
}
