using System;
using System.IO;
using System.Text.Json;
using GidEditor.Models;

namespace GidEditor.Services;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON in the per-user config directory.</summary>
public sealed class SettingsService
{
    private readonly string _path;

    public SettingsService(string path) => _path = path;

    public SettingsService()
    {
        var baseDir = OperatingSystem.IsMacOS()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _path = Path.Combine(baseDir, "GidEditor", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // A corrupt settings file should never prevent the app from starting.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception)
        {
            // Settings are a convenience; failing to persist them is not fatal.
        }
    }
}
