using Scheduler.Application.Configuration;
using Scheduler.Infrastructure.Configuration;

namespace Scheduler.IntegrationTests;

/// <summary>WP-5a and WP-5b: the user settings file of this computer and finding the PostgreSQL tools.</summary>
public sealed class MachineSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gsst-settings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SettingsFile_IsCreated_SavedAndReadBack_KeepingUnknownKeys()
    {
        var file = new MachineSettingsFile(Path.Combine(_folder, MachineSettingsFile.FileName));
        file.EnsureExists();
        Assert.Equal(new MachineSettings(), file.Get());

        await File.WriteAllTextAsync(file.FilePath, """{ "Custom": { "Keep": "me" }, "Paths": { "BackupFolder": "D:\\Old" } }""");
        await file.SaveAsync(new MachineSettings { BackupFolder = "E:\\Backups", BackupCopyFolder = "F:\\Copies", AllowLoginBypass = true });

        var read = file.Get();
        Assert.Equal("E:\\Backups", read.BackupFolder);
        Assert.Equal("F:\\Copies", read.BackupCopyFolder);
        Assert.True(read.AllowLoginBypass);
        Assert.Contains("\"Keep\": \"me\"", await File.ReadAllTextAsync(file.FilePath));
    }

    [Fact]
    public void Tools_AreFoundInTheSettingThenThePathThenTheUsualFolders()
    {
        var tool = PostgresTools.ToolFile("pg_dump");
        var setting = Path.Combine(_folder, "setting");
        var onPath = Path.Combine(_folder, "path");
        var usual = Path.Combine(_folder, "usual");
        var existing = new HashSet<string> { Path.Combine(setting, tool), Path.Combine(onPath, tool), Path.Combine(usual, tool) };
        bool Exists(string file) => existing.Contains(file);

        Assert.Equal(setting, PostgresTools.FindFolder(setting, Exists, onPath, [usual]));
        Assert.Equal(onPath, PostgresTools.FindFolder(null, Exists, "/nothing" + Path.PathSeparator + onPath, [usual]));
        Assert.Equal(usual, PostgresTools.FindFolder("", Exists, "/nothing", [usual]));
        Assert.Null(PostgresTools.FindFolder(Path.Combine(_folder, "wrong"), Exists, onPath, [usual]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
