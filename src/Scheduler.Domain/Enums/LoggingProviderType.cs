namespace Scheduler.Domain.Enums;

/// <summary>Kind of logging destination (FR-29). Custom providers come from modules.</summary>
public enum LoggingProviderType
{
    Console = 1,
    File = 2,
    Database = 3,
    Custom = 4
}
