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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly object _lock = new();
    private readonly string _settingsPath;
    private AppSettings _settings;

    private int _deferDepth;
    private bool _saveDeferred;

    public AppSettings Settings => _settings;

    /// <summary>Raised after settings.json has actually been written (once per deferred batch).</summary>
    public event Action? Saved;

    /// <summary>Serializes with the same options as settings.json.</summary>
    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    /// <summary>Reads JSON written by <see cref="ToJson{T}"/>; null when it cannot be read.</summary>
    public static T? FromJson<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public SettingsService()
        : this(AppPaths.SettingsFile)
    {
    }

    public SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;

        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _settings = Load();
    }

    private AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

            if (loaded is not null)
                return loaded;

            Log.Warn($"settings.json deserialized to null; starting from defaults ({_settingsPath}).");
        }
        catch (Exception ex)
        {
            Log.Error($"Could not read '{_settingsPath}'; starting from defaults.", ex);
        }

        // Never silently discard a layout we failed to parse — keep it aside so
        // it can be inspected or recovered by hand.
        PreserveUnreadableSettings();
        return new AppSettings();
    }

    private void PreserveUnreadableSettings()
    {
        try
        {
            var backup = _settingsPath + ".corrupt";
            File.Copy(_settingsPath, backup, overwrite: true);
            Log.Warn($"Previous settings kept at '{backup}'.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not preserve the unreadable settings file.", ex);
        }
    }

    /// <summary>
    /// Suppresses writes until the returned scope is disposed, then writes once
    /// if anything asked to be saved. Bulk work — collecting a whole desktop
    /// into a paddock, say — would otherwise rewrite settings.json per item.
    /// Scopes may be nested.
    /// </summary>
    public IDisposable DeferSave()
    {
        lock (_lock)
        {
            _deferDepth++;
        }

        return new DeferScope(this);
    }

    public void Save()
    {
        bool wrote;

        lock (_lock)
        {
            if (_deferDepth > 0)
            {
                _saveDeferred = true;
                return;
            }

            wrote = WriteToDisk();
        }

        if (wrote)
            Saved?.Invoke();
    }

    public CorralModel GetActiveCorral()
    {
        if (_settings.Corrals.TryGetValue(_settings.ActiveCorral, out var corral))
            return corral;

        var defaultCorral = new CorralModel();
        _settings.Corrals[_settings.ActiveCorral] = defaultCorral;
        return defaultCorral;
    }

    /// <summary>Must be called while holding <see cref="_lock"/>. Returns true when the file was written.</summary>
    private bool WriteToDisk()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            var tempPath = _settingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Atomic settings write failed ({ex.Message}); trying a direct write.");

            try
            {
                var json = JsonSerializer.Serialize(_settings, JsonOptions);
                File.WriteAllText(_settingsPath, json);
                return true;
            }
            catch (Exception fallbackEx)
            {
                Log.Error("Could not save settings; will retry on the next change.", fallbackEx);
                return false;
            }
        }
    }

    private void EndDefer()
    {
        var wrote = false;

        lock (_lock)
        {
            if (_deferDepth > 0)
                _deferDepth--;

            if (_deferDepth > 0 || !_saveDeferred)
                return;

            _saveDeferred = false;
            wrote = WriteToDisk();
        }

        if (wrote)
            Saved?.Invoke();
    }

    private sealed class DeferScope(SettingsService owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            owner.EndDefer();
        }
    }
}
