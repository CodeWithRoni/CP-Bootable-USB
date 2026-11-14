using System.IO;
using System.Text.Json;
using CP.Shared;
using CP.Shared.Models;

namespace UsbMaker.Services;

public sealed class UserSettings
{
    public CpConfig? LastConfig { get; set; }
    public string? LastLogoPath { get; set; }
    public string? PayloadPath { get; set; }
    public string? DataFileSystem { get; set; }
    public int? BootPartitionSizeMb { get; set; }
}

/// <summary>Remembers the last-used branding on the technician PC (%AppData%\CP Bootable USB\settings.json).</summary>
public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CP Bootable USB", "settings.json");

    public UserSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path), ConfigService.JsonOptions) ?? new UserSettings();
        }
        catch { /* corrupt settings → start fresh */ }
        return new UserSettings();
    }

    public void Save(UserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, ConfigService.JsonOptions));
        }
        catch { /* non-critical */ }
    }
}
