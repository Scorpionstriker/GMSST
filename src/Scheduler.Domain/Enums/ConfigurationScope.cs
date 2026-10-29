namespace Scheduler.Domain.Enums;

/// <summary>Scope of a configuration setting. A setting is Global exactly when it has no task (BR-10).</summary>
public enum ConfigurationScope
{
    Global = 1,
    Task = 2
}
