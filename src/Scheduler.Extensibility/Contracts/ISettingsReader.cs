namespace Scheduler.Extensibility.Contracts;

/// <summary>
/// Read access to the global settings of the configuration table (FR-36), for modules. Values are
/// cached in memory and refreshed when they change (FR-39).
/// </summary>
public interface ISettingsReader
{
    /// <summary>Returns the global value of <paramref name="key"/>, or <paramref name="defaultValue"/> when it is not set.</summary>
    Task<int> GetIntAsync(string key, int defaultValue, CancellationToken cancellationToken = default);
}
