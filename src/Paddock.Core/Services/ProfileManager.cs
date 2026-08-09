using System.Text.Json;
using System.Text.Json.Serialization;
using Paddock.Core.Models;

namespace Paddock.Core.Services;

public class ProfileManager
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly SettingsService _settingsService;

    public ProfileManager(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Snapshot the current active corral's paddocks into a new or existing named corral.
    /// </summary>
    public void SaveCorral(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var settings = _settingsService.Settings;
        var activeCorral = _settingsService.GetActiveCorral();

        var clonedPaddocks = DeepClonePaddocks(activeCorral.Paddocks);

        if (settings.Corrals.TryGetValue(name, out var existing))
        {
            existing.Paddocks = clonedPaddocks;
        }
        else
        {
            settings.Corrals[name] = new CorralModel
            {
                Name = name,
                Paddocks = clonedPaddocks
            };
        }

        _settingsService.Save();
    }

    /// <summary>
    /// Switch the active corral, returning its paddock list.
    /// </summary>
    public List<PaddockModel> LoadCorral(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var settings = _settingsService.Settings;

        if (!settings.Corrals.ContainsKey(name))
            throw new KeyNotFoundException($"Corral '{name}' does not exist.");

        settings.ActiveCorral = name;
        _settingsService.Save();

        return settings.Corrals[name].Paddocks;
    }

    /// <summary>
    /// Remove a saved corral. Cannot delete the currently active corral.
    /// </summary>
    public void DeleteCorral(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var settings = _settingsService.Settings;

        if (string.Equals(settings.ActiveCorral, name, StringComparison.Ordinal))
            throw new InvalidOperationException("Cannot delete the active corral.");

        if (!settings.Corrals.Remove(name))
            throw new KeyNotFoundException($"Corral '{name}' does not exist.");

        _settingsService.Save();
    }

    /// <summary>
    /// List all saved corral names.
    /// </summary>
    public List<string> GetCorralNames()
    {
        return _settingsService.Settings.Corrals.Keys.ToList();
    }

    /// <summary>
    /// Copy a corral under a new name.
    /// </summary>
    public void DuplicateCorral(string sourceName, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var settings = _settingsService.Settings;

        if (!settings.Corrals.TryGetValue(sourceName, out var source))
            throw new KeyNotFoundException($"Corral '{sourceName}' does not exist.");

        if (settings.Corrals.ContainsKey(newName))
            throw new InvalidOperationException($"Corral '{newName}' already exists.");

        settings.Corrals[newName] = new CorralModel
        {
            Name = newName,
            Paddocks = DeepClonePaddocks(source.Paddocks)
        };

        _settingsService.Save();
    }

    /// <summary>
    /// Export a corral as a JSON .paddock file.
    /// </summary>
    public void ExportCorral(string name, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var settings = _settingsService.Settings;

        if (!settings.Corrals.TryGetValue(name, out var corral))
            throw new KeyNotFoundException($"Corral '{name}' does not exist.");

        var json = JsonSerializer.Serialize(corral, ExportJsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Import a corral from a .paddock JSON file. Returns the imported corral name.
    /// </summary>
    public string ImportCorral(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Import file not found.", filePath);

        var json = File.ReadAllText(filePath);
        var imported = JsonSerializer.Deserialize<CorralModel>(json, ExportJsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize corral from file.");

        if (string.IsNullOrWhiteSpace(imported.Name))
            imported.Name = Path.GetFileNameWithoutExtension(filePath);

        var settings = _settingsService.Settings;
        var name = imported.Name;

        // Avoid overwriting an existing corral by appending a suffix. An empty
        // corral (the pristine "default" of a fresh install) is filled in place
        // instead of being treated as a conflict.
        var baseName = name;
        var counter = 1;
        while (settings.Corrals.TryGetValue(name, out var conflict) && conflict.Paddocks.Count > 0)
        {
            name = $"{baseName}_{counter}";
            counter++;
        }

        imported.Name = name;
        settings.Corrals[name] = imported;
        _settingsService.Save();

        return name;
    }

    private static List<PaddockModel> DeepClonePaddocks(List<PaddockModel> paddocks)
    {
        var json = JsonSerializer.Serialize(paddocks, ExportJsonOptions);
        return JsonSerializer.Deserialize<List<PaddockModel>>(json, ExportJsonOptions) ?? new();
    }
}
