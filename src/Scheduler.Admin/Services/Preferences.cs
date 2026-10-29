using System.Text.Json;

namespace Scheduler.Admin.Services;

/// <summary>Small per-user settings of the Admin application itself, such as the theme. Not scheduler settings.</summary>
public interface IPreferenceStore
{
    string? Get(string key);
    void Set(string key, string value);
}

/// <summary>Keeps preferences in a JSON file in the user's local application data folder.</summary>
public sealed class FilePreferenceStore : IPreferenceStore
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private Dictionary<string, string> _values;

    public FilePreferenceStore(string? folder = null)
    {
        folder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GMSS");
        _path = Path.Combine(folder, "admin-preferences.json");
        _values = Load(_path);
    }

    public string? Get(string key)
    {
        lock (_gate)
        {
            return _values.GetValueOrDefault(key);
        }
    }

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            _values[key] = value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(_values));
            }
            catch (IOException)
            {
                // A preference that cannot be saved is used for this session only.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
