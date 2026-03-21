using System.Text.Json;
using System.Text.Json.Serialization;
using Paddock.Core.Models;

namespace Paddock.Core.Services;

public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _settingsPath;
    private AppSettings _settings;

    public AppSettings Settings => _settings;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var paddockDir = Path.Combine(appData, "Paddock");
        Directory.CreateDirectory(paddockDir);
        _settingsPath = Path.Combine(paddockDir, "settings.json");

        _settings = Load();
    }

    public SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
        _settings = Load();
    }

    private AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    public CorralModel GetActiveCorral()
    {
        if (_settings.Corrals.TryGetValue(_settings.ActiveCorral, out var corral))
            return corral;

        var defaultCorral = new CorralModel();
        _settings.Corrals[_settings.ActiveCorral] = defaultCorral;
        return defaultCorral;
    }
}
