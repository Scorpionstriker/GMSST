using Scheduler.Domain.Enums;

namespace Scheduler.Domain.Entities;

/// <summary>A configured logging destination with its minimum level (FR-29). Stored in logging_provider.</summary>
public sealed class LoggingProviderSetting
{
    public const int NameMaxLength = 50;

    private LoggingProviderSetting()
    {
        ProviderName = string.Empty;
        Settings = "{}";
    }

    public LoggingProviderSetting(string providerName, LoggingProviderType providerType, SchedulerLogLevel minimumLevel,
        bool isActive, string settingsJson = "{}")
    {
        ProviderName = providerName;
        ProviderType = providerType;
        MinimumLevel = minimumLevel;
        IsActive = isActive;
        Settings = settingsJson;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string ProviderName { get; private set; }
    public LoggingProviderType ProviderType { get; private set; }
    public SchedulerLogLevel MinimumLevel { get; private set; }

    /// <summary>Provider-specific settings as JSON, for example a file path.</summary>
    public string Settings { get; private set; }

    public bool IsActive { get; private set; }

    public void Change(bool isActive, SchedulerLogLevel minimumLevel)
    {
        IsActive = isActive;
        MinimumLevel = minimumLevel;
    }
}
