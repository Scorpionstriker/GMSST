using Scheduler.Domain.Enums;

namespace Scheduler.Domain.Entities;

/// <summary>
/// A key-value setting, global or for one task, with exactly one value per key and scope
/// (FR-36, BR-10). Values are validated against their key's type and range before they are stored (FR-37).
/// </summary>
public sealed class ConfigurationSetting
{
    public const int KeyMaxLength = 50;
    public const int ValueMaxLength = 255;

    private ConfigurationSetting()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    private ConfigurationSetting(Guid? taskId, string key, string value, DateTime nowUtc)
    {
        TaskId = taskId;
        Key = key;
        Value = value;
        Scope = taskId is null ? ConfigurationScope.Global : ConfigurationScope.Task;
        UpdatedAtUtc = nowUtc;
    }

    public static ConfigurationSetting Global(string key, string value, DateTime nowUtc) => new(null, key, value, nowUtc);

    public static ConfigurationSetting ForTask(Guid taskId, string key, string value, DateTime nowUtc) => new(taskId, key, value, nowUtc);

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid? TaskId { get; private set; }
    public string Key { get; private set; }
    public string Value { get; private set; }
    public ConfigurationScope Scope { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void ChangeValue(string value, DateTime nowUtc)
    {
        Value = value;
        UpdatedAtUtc = nowUtc;
    }
}
