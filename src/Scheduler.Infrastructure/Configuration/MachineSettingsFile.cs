using System.Text.Json;
using System.Text.Json.Nodes;
using Scheduler.Application.Configuration;

namespace Scheduler.Infrastructure.Configuration;

/// <summary>
/// The user settings file of this computer (WP-5a): %LOCALAPPDATA%\GSST\appsettings.user.json on Windows and
/// ~/Library/Application Support/GSST/appsettings.user.json on macOS. It is read as a configuration source after
/// the embedded appsettings.json, so its values win, and environment variables starting with GSST_ win over both.
/// It never holds a password. Keys the application does not know are kept when the file is saved.
/// </summary>
public sealed class MachineSettingsFile(string filePath) : IMachineSettingsStore
{
    public const string FileName = "appsettings.user.json";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public string FilePath { get; } = filePath;

    /// <summary>The default location for the current user.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "GSST", FileName);

    /// <summary>Creates the file with empty values on first start, so the user can find and edit it.</summary>
    public void EnsureExists()
    {
        if (File.Exists(FilePath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, ToJson(new MachineSettings(), new JsonObject()));
    }

    public MachineSettings Get()
    {
        var root = Read();
        return new MachineSettings
        {
            PostgresBinFolder = Text(root, "Paths", "PostgresBinFolder"),
            BackupFolder = Text(root, "Paths", "BackupFolder"),
            BackupCopyFolder = Text(root, "Paths", "BackupCopyFolder"),
            LogFolder = Text(root, "Paths", "LogFolder"),
            AllowLoginBypass = root["Security"]?["AllowLoginBypass"] is JsonValue bypass && bypass.TryGetValue<bool>(out var on) && on,
            MaintenanceAgentUrl = Text(root, "MaintenanceAgent", "Url")
        };
    }

    public async Task SaveAsync(MachineSettings settings, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".tmp";
            await File.WriteAllTextAsync(temporary, ToJson(settings, Read()), cancellationToken);
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            Gate.Release();
        }
    }

    private JsonObject Read()
    {
        try
        {
            return File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath),
                       documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is JsonObject root
                ? root
                : new JsonObject();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new JsonObject();
        }
    }

    private static string Text(JsonObject root, string section, string key) =>
        root[section]?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

    private static string ToJson(MachineSettings settings, JsonObject root)
    {
        Section(root, "Paths")["PostgresBinFolder"] = settings.PostgresBinFolder;
        Section(root, "Paths")["BackupFolder"] = settings.BackupFolder;
        Section(root, "Paths")["BackupCopyFolder"] = settings.BackupCopyFolder;
        Section(root, "Paths")["LogFolder"] = settings.LogFolder;
        Section(root, "Security")["AllowLoginBypass"] = settings.AllowLoginBypass;
        Section(root, "MaintenanceAgent")["Url"] = settings.MaintenanceAgentUrl;
        return root.ToJsonString(WriteOptions);
    }

    private static JsonObject Section(JsonObject root, string name)
    {
        if (root[name] is JsonObject section)
        {
            return section;
        }

        section = new JsonObject();
        root[name] = section;
        return section;
    }
}
